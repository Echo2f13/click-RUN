# API Reference

Internal class and component documentation for contributors.

## Models

### `Configuration`
Root configuration model. All properties have sensible defaults.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ScanIntervalMs` | `int` | `500` | Scan interval in ms (clamped to 300-800) |
| `DebounceCooldownMs` | `int` | `2000` | Debounce cooldown in ms |
| `KillSwitchHotkey` | `string` | `"Ctrl+Alt+R"` | Global hotkey string |
| `LogLevel` | `string` | `"info"` | Log level (debug/info/warn/error) |
| `EnableWildcardProcess` | `bool` | `false` | Allow wildcard process matching |
| `EnableDebugInstrumentation` | `bool` | `false` | Per-element debug logging |
| `DryRun` | `bool` | `false` | Simulate clicks without executing |
| `PreClickDelayMs` | `int` | `0` | Pre-click delay in ms (clamped to 0-200) |
| `BlockedLabels` | `List<string>` | `["Reject", "Cancel", "Deny"]` | Hard-rejected label substrings |
| `MultiWindowMode` | `bool` | `false` | Scan all whitelisted windows vs foreground only |
| `Whitelist` | `List<WhitelistEntry>` | `[]` | Target application entries |

### `WhitelistEntry`
| Property | Type | Description |
|----------|------|-------------|
| `ProcessName` | `string` | Windows process name (case-insensitive) |
| `WindowTitles` | `List<WindowTitlePattern>` | Window title patterns to match |
| `ButtonLabels` | `List<string>` | Allowed button labels (order defines keyword priority) |

### `WindowTitlePattern`
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Pattern` | `string` | `""` | Pattern string |
| `MatchMode` | `MatchMode` | `Exact` | Matching strategy |

### `MatchMode` (enum)
| Value | Description |
|-------|-------------|
| `Exact` | Full case-insensitive string equality |
| `Contains` | Case-insensitive substring check |
| `Regex` | Regex match with 100ms timeout |

### `ElementDescriptor` (record)
Describes a UI element candidate. Immutable.

| Property | Type | Description |
|----------|------|-------------|
| `ProcessName` | `string` | Source process name |
| `WindowTitle` | `string` | Source window title |
| `ButtonLabel` | `string` | Button's Name property |
| `AutomationId` | `string` | Button's AutomationId |
| `IsButton` | `bool` | Whether control type is Button |
| `IsVisible` | `bool` | Whether element is visible |
| `IsEnabled` | `bool` | Whether element is enabled |

### `Candidate` (record)
A button that passed the safety filter, ready for prioritization.

| Property | Type | Description |
|----------|------|-------------|
| `Element` | `ElementDescriptor` | The element descriptor |
| `MatchedEntry` | `WhitelistEntry` | Which whitelist entry matched |
| `Hash` | `string` | Debounce hash (32 hex chars) |

## Components

### `Detector`
```csharp
public ScanResult? Scan()
```
Scans the foreground window only. Returns `null` if no window, process gone, or API error.

```csharp
public List<ScanResult> ScanAll(HashSet<string> whitelistedProcessNames)
```
Scans all visible windows belonging to whitelisted processes. Uses `EnumWindows` + `IsWindowVisible` + `GetWindowThreadProcessId`. Skips windows with empty titles. Logs diagnostic info per cycle.

### `SafetyFilter`
```csharp
public SafetyFilterResult Check(ElementDescriptor element, Configuration config)
```
Returns `SafetyFilterResult` with `Passed`, `MatchedEntry`, and `RejectionReason`.

Rejection reasons: `not_button`, `not_visible`, `not_enabled`, `blocked_label`, `process_mismatch`, `title_mismatch`, `label_mismatch`.

### `ButtonPrioritizer`
```csharp
public static Candidate? SelectBest(List<Candidate> candidates, List<WhitelistEntry> whitelist)
```
Returns the single highest-priority candidate, or `null` if empty. Primary ranking: whitelist label index (keyword priority). Secondary: exact match beats substring.

### `Clicker`
```csharp
public ClickResult Click(AutomationElement button, ElementDescriptor descriptor, int preClickDelayMs = 0)
```
Clicks via InvokePattern with optional pre-click delay and single retry. Returns `ClickResult` with `Success` and optional `ErrorMessage`.

### `TitleMatcher`
```csharp
public static bool Match(string windowTitle, string pattern, MatchMode mode)
public static bool MatchAny(string windowTitle, List<WindowTitlePattern> patterns)
```

### `DebounceTracker`
```csharp
public static string ComputeHash(ElementDescriptor element)  // SHA256, 32 hex chars
public bool IsInCooldown(string hash, TimeSpan cooldown)
public void Record(string hash)
public void Prune()  // removes entries > 10 seconds old
```
Handles null/empty AutomationId gracefully — computes hash without it.

### `KillSwitch` (IDisposable)
```csharp
public bool IsEnabled { get; }  // volatile, thread-safe
```
Constructor registers the hotkey. `Dispose()` unregisters and stops the message loop.

### `ConfigParser`
```csharp
public static Configuration Parse(string json, ILogger? logger = null)
public static Configuration? LoadFromFile(string filePath, ILogger? logger = null)
```

### `ConfigSerializer`
```csharp
public static string Serialize(Configuration config)
public static void SaveToFile(Configuration config, string filePath)
```

### `LoggerSetup`
```csharp
public static ILogger CreateLogger(string logLevel)
```

## Engine

### `ClickRunEngine` (IDisposable)
Core scan-and-click engine. Runs on a background Task.

```csharp
public ClickRunEngine(Configuration config, ILogger logger)
public void Start()        // Start scanning
public void Stop()         // Stop scanning
public void Pause()        // Pause scanning
public void Resume()       // Resume scanning
public void TogglePause()  // Toggle pause/resume
public bool IsRunning { get; }
public bool IsPaused { get; }
```

## Tray

### `TrayApp` (ApplicationContext)
System tray application shell. Hosts the engine and provides NotifyIcon with context menu.

```csharp
public TrayApp(Configuration config, ILogger logger)
```

Context menu items: Running/Paused status, Pause/Resume, Open Logs, Open Config, Start with Windows, Exit.

### `SingleInstance` (IDisposable)
Named Mutex guard ensuring only one instance runs.

```csharp
public bool IsFirstInstance { get; }
```
