# Architecture

## Overview

Click Run is a single-process Windows console application built with C# (.NET 8). It runs as a background process with no GUI, scanning windows for permission prompt buttons and clicking them via the Windows UI Automation API. It supports two scanning modes: foreground-only (default) and multi-window (scans all whitelisted process windows).

## Component Diagram

```
┌──────────────────────────────────────────────────────────┐
│                        Click Run                          │
│                                                           │
│  ┌──────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │ Detector  │─▶│ Safety Filter │─▶│ Button Prioritizer│  │
│  └────┬─────┘  └──────┬───────┘  └────────┬─────────┘   │
│       │               │                    │              │
│       ▼               ▼                    ▼              │
│  ┌─────────┐  ┌──────────────┐  ┌──────────────────┐    │
│  │ UI Auto  │  │   Debounce   │  │ Clicker (w/Retry)│    │
│  │  API     │  │   Tracker    │  └──────────────────┘    │
│  └─────────┘  └──────────────┘                           │
│                                                           │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ Config Parser │  │ Kill Switch  │  │ Title Matcher │  │
│  │ / Serializer  │  │(Global Hotkey)│  │ (exact/contains│ │
│  └──────────────┘  └──────────────┘  │  /regex)       │  │
│                                       └───────────────┘  │
│  ┌──────────────┐                                        │
│  │ Logger       │                                        │
│  │ (Serilog)    │                                        │
│  └──────────────┘                                        │
└──────────────────────────────────────────────────────────┘
```

## Components

### Detector (`Detection/Detector.cs`)

Two scanning modes:

**Foreground mode** (`Scan()`): Retrieves the foreground window handle via Win32 `GetForegroundWindow()` P/Invoke, then uses `AutomationElement.FromHandle()` to get the root UI Automation element.

**Multi-window mode** (`ScanAll()`): Uses Win32 `EnumWindows` to enumerate all visible windows, filters by whitelisted process names (via `GetWindowThreadProcessId` + PID lookup), skips windows with empty titles (helper/tooltip windows), and scans each matching window. Logs diagnostic info: total visible windows found, which matched the whitelist, and how many buttons each contained.

Both modes extract the process name (via PID lookup) and window title, then call `FindAll()` with conditions for `ControlType.Button`, `IsEnabled=true`, `IsOffscreen=false`. Builds an `ElementDescriptor` for each button found.

Returns `ScanResult` (single window) or `List<ScanResult>` (multi-window).

Handles all failure modes gracefully: null foreground window, process no longer exists, `ElementNotAvailableException`, `COMException`.

### Safety Filter (`Filtering/SafetyFilter.cs`)

Validates each candidate element through a strict pipeline:

1. Control type must be Button (`not_button`)
2. Element must be visible (`not_visible`)
3. Element must be enabled (`not_enabled`)
4. Button label must not contain any blocked word (`blocked_label`) — checked via case-insensitive substring match against `blockedLabels` config
5. Process name must match a whitelist entry (`process_mismatch`)
6. Window title must match via TitleMatcher (`title_mismatch`)
7. Button label must match a whitelist label (`label_mismatch`)

Wildcard process entries (`"*"`) are skipped when `enableWildcardProcess` is false — they don't block other entries from matching.

Returns a `SafetyFilterResult` with pass/fail, the matched whitelist entry, and a specific rejection reason string.

### Button Prioritizer (`Filtering/ButtonPrioritizer.cs`)

When multiple buttons pass the safety filter in a single scan cycle, selects the single best candidate using strict keyword priority:

**Primary ranking**: whitelist label index (earlier = higher priority). The config order defines keyword priority: `Run`(0) > `Allow`(1) > `Approve`(2) > `Accept`(5) > `Trust`(7).

**Secondary ranking**: match type — exact match beats substring match at the same label index.

Examples with default config order:
- `"Run"` (exact, index 0) beats `"Accept command"` (exact, index 6)
- `"Trust command and accept"` resolves to `"Accept"` (substring, index 5) not `"Trust command and accept"` (exact, index 8), because index 5 < index 8
- `"Run anyway"` resolves to `"Run"` (substring, index 0)

Only one button is clicked per scan cycle, even in multi-window mode.

### Clicker (`Clicking/Clicker.cs`)

Invokes the button via `InvokePattern.Invoke()` — a native UI Automation action, not mouse simulation.

Retry strategy:
1. Get `InvokePattern` from the element
2. Optional pre-click delay (configurable, 0-200ms)
3. First `Invoke()` attempt
4. On failure: wait 50-100ms (randomized), retry exactly once
5. On retry failure: log error, return failure result

### Title Matcher (`Matching/TitleMatcher.cs`)

Matches window titles against configured patterns with three modes:

- `exact`: full title must equal pattern (case-insensitive)
- `contains`: title must contain pattern as substring (case-insensitive)
- `regex`: title must match regex pattern (with 100ms timeout to prevent ReDoS)

Default mode is `exact` when not specified in config.

### Debounce Tracker (`Tracking/DebounceTracker.cs`)

Prevents duplicate clicks using a hash-based tracking system:

- Element hash: SHA256 of `processName|windowTitle|buttonLabel|automationId`, truncated to 16 bytes (32 hex chars)
- If AutomationId is null/empty, hash is computed without it
- Cooldown: rejects elements clicked within the last 2 seconds (configurable)
- Pruning: removes entries older than 10 seconds each cycle

### Kill Switch (`Hotkey/KillSwitch.cs`)

Registers a global hotkey via Win32 `RegisterHotKey` API on a dedicated background message-loop thread. Toggles a `volatile bool isEnabled` that the main loop checks before each scan.

Default hotkey: `Ctrl+Alt+R` (configurable). If registration fails (e.g., hotkey already taken), logs a warning and continues without kill switch.

### Config Parser / Serializer (`Config/`)

- `ConfigParser`: loads JSON from `~/.clickrun/config.json`, deserializes with `System.Text.Json` (camelCase, enum converter), validates regex patterns at load time, clamps `ScanIntervalMs` to [300, 800] and `PreClickDelayMs` to [0, 200]
- `ConfigSerializer`: serializes back to JSON with 2-space indentation
- `DefaultConfig`: creates default config on first run with Kiro, Code, Claude entries

### Logger (`Logging/LoggerSetup.cs`)

Serilog with file sink at `~/.clickrun/clickrun.log`. Rolling file rotation at 10 MB, keeps 3 files. Output format: `[ISO8601_TIMESTAMP] [LEVEL] [COMPONENT] message`.

## Main Loop Flow

```
Program.Main()
  ├── Load config (or create default)
  ├── Setup Serilog logger
  ├── Register kill switch hotkey
  ├── Log startup info (including MultiWindowMode)
  └── Enter scan loop:
        ├── await Task.Delay(scanIntervalMs)
        ├── Check kill switch → skip if disabled
        ├── If multiWindowMode:
        │     Detector.ScanAll(whitelistedProcesses) → List<ScanResult>
        │   Else:
        │     Detector.Scan() → single ScanResult (or null)
        ├── For each button across all scan results:
        │     ├── SafetyFilter.Check() → pass/reject with reason
        │     ├── DebounceTracker.IsInCooldown() → reject if cooling
        │     └── Add to candidates if passed
        ├── Log per-cycle summary (with rejection breakdown)
        ├── ButtonPrioritizer.SelectBest() → single best candidate
        ├── If dry run: log "[DRY RUN] Would click: ..."
        │   Else: Clicker.Click() with retry
        ├── Record debounce on success
        ├── Log click at info level
        └── DebounceTracker.Prune()
```

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Language | C# (.NET 8) |
| UI Automation | System.Windows.Automation (WindowsDesktop framework) |
| Window Enumeration | Win32 EnumWindows, IsWindowVisible, GetWindowThreadProcessId |
| Hotkey | Win32 RegisterHotKey via P/Invoke |
| Config | System.Text.Json |
| Logging | Serilog + Serilog.Sinks.File |
| Regex | System.Text.RegularExpressions (with timeout) |
| Hashing | System.Security.Cryptography.SHA256 |
| Distribution | Single self-contained executable (win-x64) |

## Project Structure

```
src/ClickRun/
├── ClickRun.csproj
├── Program.cs                    # Entry point and main scan loop
├── Clicking/
│   ├── Clicker.cs                # InvokePattern click with retry
│   └── ClickResult.cs            # Click result record
├── Config/
│   ├── ConfigParser.cs           # JSON loading and validation
│   ├── ConfigSerializer.cs       # JSON serialization
│   └── DefaultConfig.cs          # Default config creation
├── Detection/
│   ├── Detector.cs               # UI Automation scanning (foreground + multi-window)
│   └── ScanResult.cs             # Scan result record
├── Filtering/
│   ├── ButtonPrioritizer.cs      # Keyword-priority button selection
│   ├── SafetyFilter.cs           # Whitelist + blocklist validation
│   └── SafetyFilterResult.cs     # Filter result record
├── Hotkey/
│   └── KillSwitch.cs             # Global hotkey toggle
├── Logging/
│   └── LoggerSetup.cs            # Serilog configuration
├── Matching/
│   └── TitleMatcher.cs           # Window title matching (exact/contains/regex)
├── Models/
│   ├── Candidate.cs              # Candidate button record
│   ├── Configuration.cs          # Root config model
│   ├── ElementDescriptor.cs      # UI element descriptor record
│   ├── MatchMode.cs              # Match mode enum
│   ├── WhitelistEntry.cs         # Whitelist entry model
│   └── WindowTitlePattern.cs     # Window title pattern model
└── Tracking/
    └── DebounceTracker.cs        # Hash-based debounce tracking

tests/ClickRun.Tests/
├── ClickRun.Tests.csproj
├── SafetyFilterTests.cs          # 17 tests (including blocklist)
├── ButtonPrioritizerTests.cs     # 14 tests (including keyword priority)
├── DebounceTrackerTests.cs       # 14 tests (including null AutomationId)
└── LoggerSetupTests.cs           # 8 tests
```
