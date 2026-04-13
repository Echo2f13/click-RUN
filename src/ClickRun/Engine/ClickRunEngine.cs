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

namespace ClickRun.Engine;

/// <summary>
/// Core scan-and-click engine. Extracted from Program.cs so it can be
/// driven by the tray app, CLI, or tests independently.
/// </summary>
public sealed class ClickRunEngine : IDisposable
{
    private readonly Configuration _config;
    private readonly ILogger _logger;
    private readonly KillSwitch _killSwitch;
    private readonly Detector _detector;
    private readonly SafetyFilter _safetyFilter;
    private readonly DebounceTracker _debounceTracker;
    private readonly Clicker _clicker;
    private readonly KeyboardFallback _keyboardFallback;
    private readonly TimeSpan _debounceCooldown;
    private readonly HashSet<string>? _whitelistedProcesses;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile bool _paused;
    private readonly Dictionary<string, DateTime> _firstSeen = new();

    public bool IsRunning => _runTask is { IsCompleted: false };
    public bool IsPaused => _paused;

    public ClickRunEngine(Configuration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _killSwitch = new KillSwitch(config.KillSwitchHotkey, logger);
        _detector = new Detector(logger);
        _safetyFilter = new SafetyFilter(logger);
        _debounceTracker = new DebounceTracker();
        _clicker = new Clicker(logger);
        _keyboardFallback = new KeyboardFallback(logger);
        _debounceCooldown = TimeSpan.FromMilliseconds(config.DebounceCooldownMs);

        if (config.MultiWindowMode)
        {
            _whitelistedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in config.Whitelist)
            {
                if (!string.Equals(entry.ProcessName, "*", StringComparison.Ordinal))
                    _whitelistedProcesses.Add(entry.ProcessName);
            }
            _logger.Information("Multi-window mode enabled. Scanning processes: {Processes}",
                string.Join(", ", _whitelistedProcesses));
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _paused = false;
        _runTask = Task.Run(() => RunLoop(_cts.Token));
        _logger.Information("ClickRun engine started.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _logger.Information("ClickRun engine stopped.");
    }

    public void Pause()
    {
        _paused = true;
        _logger.Information("ClickRun engine paused.");
    }

    public void Resume()
    {
        _paused = false;
        _logger.Information("ClickRun engine resumed.");
    }

    public void TogglePause()
    {
        if (_paused) Resume(); else Pause();
    }

    public void Dispose()
    {
        Stop();
        _killSwitch.Dispose();
    }

    private async Task RunLoop(CancellationToken ct)
    {
        var debugInstrumentation = _config.EnableDebugInstrumentation;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_config.ScanIntervalMs, ct); }
            catch (OperationCanceledException) { break; }

            if (_paused || !_killSwitch.IsEnabled)
                continue;

            List<ScanResult> scanResults;
            if (_config.MultiWindowMode && _whitelistedProcesses != null)
            {
                scanResults = _detector.ScanAll(_whitelistedProcesses);
                if (scanResults.Count == 0) continue;
            }
            else
            {
                var single = _detector.Scan();
                if (single is null) continue;
                scanResults = new List<ScanResult> { single };
            }

            var rejectionCounters = new Dictionary<string, int>
            {
                ["process_mismatch"] = 0, ["title_mismatch"] = 0, ["label_mismatch"] = 0,
                ["not_button"] = 0, ["not_visible"] = 0, ["not_enabled"] = 0,
                ["debounce_cooldown"] = 0, ["wildcard_blocked"] = 0, ["blocked_label"] = 0,
                ["dangerous_context"] = 0, ["missing_safe_context"] = 0
            };

            var allCandidates = new List<(Candidate Candidate, ScanResult Source)>();
            int totalScanCount = 0;

            foreach (var scanResult in scanResults)
            {
                totalScanCount += scanResult.Buttons.Count;
                foreach (var (descriptor, automationElement) in scanResult.Buttons)
                {
                    var filterResult = _safetyFilter.Check(descriptor, _config);
                    if (!filterResult.Passed)
                    {
                        var reason = filterResult.RejectionReason ?? "process_mismatch";
                        if (rejectionCounters.ContainsKey(reason)) rejectionCounters[reason]++;
                        if (debugInstrumentation)
                        _logger.Debug("Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=REJECT | Reason={Reason} | Context={Context}",
                            descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId, reason,
                            descriptor.ContextText.Length > 80 ? descriptor.ContextText[..80] + "..." : descriptor.ContextText);
                        continue;
                    }

                    var hash = DebounceTracker.ComputeHash(descriptor);
                    if (_debounceTracker.IsInCooldown(hash, _debounceCooldown))
                    {
                        rejectionCounters["debounce_cooldown"]++;
                        if (debugInstrumentation)
                            _logger.Debug("Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=REJECT | Reason=debounce_cooldown",
                                descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId);
                        continue;
                    }

                    if (debugInstrumentation)
                        _logger.Debug("Element: Process={ProcessName} | Window={WindowTitle} | Label={ButtonLabel} | AutomationId={AutomationId} | Result=PASS | Context={Context}",
                            descriptor.ProcessName, descriptor.WindowTitle, descriptor.ButtonLabel, descriptor.AutomationId,
                            descriptor.ContextText.Length > 80 ? descriptor.ContextText[..80] + "..." : descriptor.ContextText);

                    allCandidates.Add((new Candidate(descriptor, filterResult.MatchedEntry!, hash), scanResult));
                }
            }

            var candidates = allCandidates.Select(c => c.Candidate).ToList();
            int rejectCount = rejectionCounters.Values.Sum();
            int clicked = 0;

            if (candidates.Count == 0)
            {
                // Keyboard fallback: if no UI Automation candidates found, try numbered options
                if (_config.EnableKeyboardFallback && scanResults.Count > 0)
                {
                    foreach (var scanResult in scanResults)
                    {
                        // Collect all context text from this window's buttons
                        var allContext = string.Join(" ",
                            scanResult.Buttons
                                .Select(b => b.Descriptor.ContextText)
                                .Where(c => !string.IsNullOrEmpty(c)));

                        if (!string.IsNullOrEmpty(allContext))
                        {
                            var sent = _keyboardFallback.TryFallback(
                                allContext,
                                scanResult.WindowTitle,
                                scanResult.ProcessName,
                                scanResult.WindowHandle,
                                _config,
                                _config.DryRun);

                            if (sent)
                            {
                                clicked = 1;
                                break;
                            }
                        }
                    }
                }

                LogSummary(totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                _debounceTracker.Prune();
                continue;
            }

            var best = ButtonPrioritizer.SelectBest(candidates, _config.Whitelist);
            if (best is null)
            {
                LogSummary(totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                _debounceTracker.Prune();
                continue;
            }

            // First-seen delay: don't click until the button has been visible for FirstSeenDelayMs
            if (_config.FirstSeenDelayMs > 0)
            {
                if (!_firstSeen.TryGetValue(best.Hash, out var firstSeenTime))
                {
                    _firstSeen[best.Hash] = DateTime.UtcNow;
                    _logger.Debug("FirstSeen: New button detected, waiting {DelayMs}ms — {Label}",
                        _config.FirstSeenDelayMs, best.Element.ButtonLabel);
                    LogSummary(totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                    _debounceTracker.Prune();
                    PruneFirstSeen();
                    continue;
                }

                var elapsed = (DateTime.UtcNow - firstSeenTime).TotalMilliseconds;
                if (elapsed < _config.FirstSeenDelayMs)
                {
                    _logger.Debug("FirstSeen: Waiting {Remaining}ms more — {Label}",
                        (int)(_config.FirstSeenDelayMs - elapsed), best.Element.ButtonLabel);
                    LogSummary(totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
                    _debounceTracker.Prune();
                    continue;
                }

                // Delay satisfied — remove from first-seen tracker and proceed to click
                _firstSeen.Remove(best.Hash);
            }

            if (_config.DryRun)
            {
                _logger.Information("[DRY RUN] Would click: {ProcessName} | {WindowTitle} | {ButtonLabel}",
                    best.Element.ProcessName, best.Element.WindowTitle, best.Element.ButtonLabel);
                _debounceTracker.Record(best.Hash);
                clicked = 1;
            }
            else
            {
                AutomationElement? bestElement = null;
                foreach (var (candidate, source) in allCandidates)
                {
                    if (candidate.Hash == best.Hash)
                    {
                        foreach (var (descriptor, automationElement) in source.Buttons)
                        {
                            if (DebounceTracker.ComputeHash(descriptor) == best.Hash)
                            { bestElement = automationElement; break; }
                        }
                        break;
                    }
                }

                if (bestElement is not null)
                {
                    var clickResult = _clicker.Click(bestElement, best.Element, _config.PreClickDelayMs);
                    if (clickResult.Success)
                    {
                        _debounceTracker.Record(best.Hash);
                        clicked = 1;
                        _logger.Information("Clicked: {ProcessName} | {WindowTitle} | {ButtonLabel}",
                            best.Element.ProcessName, best.Element.WindowTitle, best.Element.ButtonLabel);
                    }
                }
            }

            LogSummary(totalScanCount, candidates.Count, rejectCount, rejectionCounters, clicked);
            _debounceTracker.Prune();
        }
    }

    private void LogSummary(int scanCount, int passedCount, int rejectCount, Dictionary<string, int> c, int clicked)
    {
        _logger.Debug(
            "Scan: {ScanCount} elements, {PassedCount} passed, {RejectCount} rejected (process:{Process}, title:{Title}, label:{Label}, blocked:{Blocked}, not_button:{NotButton}, not_visible:{NotVisible}, not_enabled:{NotEnabled}, debounce:{Debounce}, wildcard:{Wildcard}), clicked:{Clicked}",
            scanCount, passedCount, rejectCount,
            c["process_mismatch"], c["title_mismatch"], c["label_mismatch"], c["blocked_label"],
            c["not_button"], c["not_visible"], c["not_enabled"],
            c["debounce_cooldown"], c["wildcard_blocked"], clicked);
    }

    private void PruneFirstSeen()
    {
        // Remove entries older than 10 seconds (button disappeared before delay was met)
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        var stale = _firstSeen.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in stale)
            _firstSeen.Remove(key);
    }
}
