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
                "DebugInstrumentation={DebugInstrumentation}, DryRun={DryRun}, PreClickDelayMs={PreClickDelayMs}, " +
                "MultiWindowMode={MultiWindowMode}",
                config.ScanIntervalMs,
                config.DebounceCooldownMs,
                config.KillSwitchHotkey,
                config.EnableWildcardProcess,
                config.Whitelist.Count,
                config.EnableDebugInstrumentation,
                config.DryRun,
                config.PreClickDelayMs,
                config.MultiWindowMode);

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

        // Pre-compute whitelisted process names for multi-window mode
        HashSet<string>? whitelistedProcesses = null;
        if (config.MultiWindowMode)
        {
            whitelistedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in config.Whitelist)
            {
                if (!string.Equals(entry.ProcessName, "*", StringComparison.Ordinal))
                    whitelistedProcesses.Add(entry.ProcessName);
            }
            logger.Information("Multi-window mode enabled. Scanning processes: {Processes}", string.Join(", ", whitelistedProcesses));
        }

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

            if (!killSwitch.IsEnabled)
            {
                continue;
            }

            // Collect scan results — single window or multi-window
            List<ScanResult> scanResults;
            if (config.MultiWindowMode && whitelistedProcesses != null)
            {
                scanResults = detector.ScanAll(whitelistedProcesses);
                if (scanResults.Count == 0)
                    continue;
            }
            else
            {
                var single = detector.Scan();
                if (single is null)
                    continue;
                scanResults = new List<ScanResult> { single };
            }

            // Process all scan results, collect candidates across all windows
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

            var allCandidates = new List<(Candidate Candidate, ScanResult Source)>();
            int totalScanCount = 0;

            foreach (var scanResult in scanResults)
            {
                totalScanCount += scanResult.Buttons.Count;

                foreach (var (descriptor, automationElement) in scanResult.Buttons)
                {
                    var filterResult = safetyFilter.Check(descriptor, config);
                    if (!filterResult.Passed)
                    {
                        var reason = filterResult.RejectionReason ?? "process_mismatch";
                        if (rejectionCounters.ContainsKey(reason))
                            rejectionCounters[reason]++;

                        if (debugInstrumentation)
                        {
                            logger.Debug(
                                "Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=REJECT | Reason={Reason}",
                                descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId, reason);
                        }

                        continue;
                    }

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

                    if (debugInstrumentation)
                    {
                        logger.Debug(
                            "Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=PASS | Reason=",
                            descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId);
                    }

                    var candidate = new Candidate(descriptor, filterResult.MatchedEntry!, hash);
                    allCandidates.Add((candidate, scanResult));
                }
            }

            var candidates = allCandidates.Select(c => c.Candidate).ToList();
            int rejectCount = rejectionCounters.Values.Sum();
            int clicked = 0;

            if (candidates.Count == 0 && !cancellationToken.IsCancellationRequested)
            {
                LogCycleSummary(logger, totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                debounceTracker.Prune();
                continue;
            }

            var best = ButtonPrioritizer.SelectBest(candidates, config.Whitelist);
            if (best is null)
            {
                LogCycleSummary(logger, totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                debounceTracker.Prune();
                continue;
            }

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
                // Find the AutomationElement from the source ScanResult
                AutomationElement? bestElement = null;
                foreach (var (candidate, source) in allCandidates)
                {
                    if (candidate.Hash == best.Hash)
                    {
                        foreach (var (descriptor, automationElement) in source.Buttons)
                        {
                            if (DebounceTracker.ComputeHash(descriptor) == best.Hash)
                            {
                                bestElement = automationElement;
                                break;
                            }
                        }
                        break;
                    }
                }

                if (bestElement is not null)
                {
                    var clickResult = clicker.Click(bestElement, best.Element, config.PreClickDelayMs);

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

            LogCycleSummary(logger, totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
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
