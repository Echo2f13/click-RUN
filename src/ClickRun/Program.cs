using System.Windows.Automation;
using ClickRun.Clicking;
using ClickRun.Config;
using ClickRun.Detection;
using ClickRun.Filtering;
using ClickRun.Hotkey;
using ClickRun.Logging;
using ClickRun.Models;
using ClickRun.Tracking;
using Serilog;

namespace ClickRun;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 10.1: Load config, validate, setup logger, register hotkey
        ILogger? logger = null;
        KillSwitch? killSwitch = null;

        try
        {
            var config = DefaultConfig.LoadOrCreateDefault();
            logger = LoggerSetup.CreateLogger(config.LogLevel);

            // 10.8: Log startup info
            logger.Information("ClickRun starting on platform: Windows");
            logger.Information(
                "Config loaded — ScanInterval={ScanIntervalMs}ms, DebounceCooldown={DebounceCooldownMs}ms, " +
                "KillSwitch={KillSwitchHotkey}, WildcardEnabled={EnableWildcard}, WhitelistEntries={WhitelistCount}, " +
                "DebugInstrumentation={DebugInstrumentation}, DryRun={DryRun}, PreClickDelayMs={PreClickDelayMs}",
                config.ScanIntervalMs,
                config.DebounceCooldownMs,
                config.KillSwitchHotkey,
                config.EnableWildcardProcess,
                config.Whitelist.Count,
                config.EnableDebugInstrumentation,
                config.DryRun,
                config.PreClickDelayMs);

            // Register kill switch hotkey
            killSwitch = new KillSwitch(config.KillSwitchHotkey, logger);
            logger.Information("Kill switch hotkey registered: {Hotkey}", config.KillSwitchHotkey);

            // 10.7: Graceful shutdown via CancellationToken
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                logger.Information("Shutdown signal received (Ctrl+C). Shutting down...");
                cts.Cancel();
            };

            // Initialize components
            var detector = new Detector(logger);
            var safetyFilter = new SafetyFilter(logger);
            var debounceTracker = new DebounceTracker();
            var clicker = new Clicker(logger);
            var debounceCooldown = TimeSpan.FromMilliseconds(config.DebounceCooldownMs);

            logger.Information("ClickRun is now running. Press Ctrl+C to exit.");

            // 10.2: Main scan loop
            await RunScanLoop(config, logger, killSwitch, detector, safetyFilter,
                debounceTracker, clicker, debounceCooldown, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — fall through to cleanup
        }
        catch (InvalidOperationException ex)
        {
            // Config parse errors (invalid JSON, invalid regex)
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }
        catch (Exception ex)
        {
            logger?.Error(ex, "Fatal error in ClickRun");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Environment.ExitCode = 1;
        }
        finally
        {
            // 10.7: Graceful shutdown — unregister hotkey, flush logs, exit within 2 seconds
            killSwitch?.Dispose();
            logger?.Information("ClickRun shut down.");
            Log.CloseAndFlush();
        }
    }

    private static async Task RunScanLoop(
        Configuration config,
        ILogger logger,
        KillSwitch killSwitch,
        Detector detector,
        SafetyFilter safetyFilter,
        DebounceTracker debounceTracker,
        Clicker clicker,
        TimeSpan debounceCooldown,
        CancellationToken cancellationToken)
    {
        var debugInstrumentation = config.EnableDebugInstrumentation;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(config.ScanIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // 10.2: Check kill switch
            if (!killSwitch.IsEnabled)
            {
                continue;
            }

            // Scan foreground window
            var scanResult = detector.Scan();
            if (scanResult is null)
            {
                continue;
            }

            // 12.6: Rejection reason tracking per cycle
            var rejectionCounters = new Dictionary<string, int>
            {
                ["process_mismatch"] = 0,
                ["title_mismatch"] = 0,
                ["label_mismatch"] = 0,
                ["not_button"] = 0,
                ["not_visible"] = 0,
                ["not_enabled"] = 0,
                ["debounce_cooldown"] = 0,
                ["wildcard_blocked"] = 0,
                ["blocked_label"] = 0
            };

            // 10.2–10.4: Filter and build candidates
            var candidates = new List<Candidate>();
            int scanCount = scanResult.Buttons.Count;

            foreach (var (descriptor, automationElement) in scanResult.Buttons)
            {
                // 10.3: Safety filter (wildcard check is integrated inside SafetyFilter.Check)
                var filterResult = safetyFilter.Check(descriptor, config);
                if (!filterResult.Passed)
                {
                    var reason = filterResult.RejectionReason ?? "process_mismatch";
                    if (rejectionCounters.ContainsKey(reason))
                        rejectionCounters[reason]++;

                    // 12.7: Debug instrumentation — per-element detail
                    if (debugInstrumentation)
                    {
                        logger.Debug(
                            "Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=REJECT | Reason={Reason}",
                            descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId, reason);
                    }

                    continue;
                }

                // Debounce check
                var hash = DebounceTracker.ComputeHash(descriptor);
                if (debounceTracker.IsInCooldown(hash, debounceCooldown))
                {
                    rejectionCounters["debounce_cooldown"]++;

                    if (debugInstrumentation)
                    {
                        logger.Debug(
                            "Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=REJECT | Reason=debounce_cooldown",
                            descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId);
                    }

                    continue;
                }

                // 12.7: Debug instrumentation — passed element
                if (debugInstrumentation)
                {
                    logger.Debug(
                        "Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=PASS | Reason=",
                        descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId);
                }

                candidates.Add(new Candidate(descriptor, filterResult.MatchedEntry!, hash));
            }

            int rejectCount = rejectionCounters.Values.Sum();
            int clicked = 0;

            // 12.6: Enhanced debug summary with breakdown by reason
            if (candidates.Count == 0 && !cancellationToken.IsCancellationRequested)
            {
                LogCycleSummary(logger, scanCount, candidates.Count, rejectCount, rejectionCounters, clicked);

                debounceTracker.Prune();
                continue;
            }

            // 10.4: Button prioritization — select single best candidate
            var best = ButtonPrioritizer.SelectBest(candidates, config.Whitelist);
            if (best is null)
            {
                LogCycleSummary(logger, scanCount, candidates.Count, rejectCount, rejectionCounters, clicked);

                debounceTracker.Prune();
                continue;
            }

            // 12.8: Dry run mode
            if (config.DryRun)
            {
                logger.Information(
                    "[DRY RUN] Would click: {ProcessName} | {WindowTitle} | {ButtonLabel}",
                    best.Element.ProcessName, best.Element.WindowTitle, best.Element.ButtonLabel);
                debounceTracker.Record(best.Hash);
                clicked = 1;
            }
            else
            {
                // 10.5: Click with retry — find the matching AutomationElement
                AutomationElement? bestElement = null;
                foreach (var (descriptor, automationElement) in scanResult.Buttons)
                {
                    if (DebounceTracker.ComputeHash(descriptor) == best.Hash)
                    {
                        bestElement = automationElement;
                        break;
                    }
                }

                if (bestElement is not null)
                {
                    // 12.9: Pass pre-click delay to clicker
                    var clickResult = clicker.Click(bestElement, best.Element, config.PreClickDelayMs);

                    // 10.5: Debounce recording on success, info logging on success
                    if (clickResult.Success)
                    {
                        debounceTracker.Record(best.Hash);
                        clicked = 1;
                        logger.Information(
                            "Clicked: {ProcessName} | {WindowTitle} | {ButtonLabel}",
                            best.Element.ProcessName,
                            best.Element.WindowTitle,
                            best.Element.ButtonLabel);
                    }
                }
            }

            // 12.6: Enhanced debug summary
            LogCycleSummary(logger, scanCount, candidates.Count, rejectCount, rejectionCounters, clicked);

            debounceTracker.Prune();
        }
    }

    private static void LogCycleSummary(ILogger logger, int scanCount, int passedCount, int rejectCount, Dictionary<string, int> counters, int clicked)
    {
        logger.Debug(
            "Scan: {ScanCount} elements, {PassedCount} passed, {RejectCount} rejected (process:{Process}, title:{Title}, label:{Label}, blocked:{Blocked}, not_button:{NotButton}, not_visible:{NotVisible}, not_enabled:{NotEnabled}, debounce:{Debounce}, wildcard:{Wildcard}), clicked:{Clicked}",
            scanCount, passedCount, rejectCount,
            counters["process_mismatch"], counters["title_mismatch"], counters["label_mismatch"],
            counters["blocked_label"],
            counters["not_button"], counters["not_visible"], counters["not_enabled"],
            counters["debounce_cooldown"], counters["wildcard_blocked"], clicked);
    }
}
