# Architecture

## Overview

Click Run is a single-process Windows console application built with C# (.NET 8). It runs as a background process with no GUI, scanning the foreground window for permission prompt buttons and clicking them via the Windows UI Automation API.

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

Retrieves the foreground window handle via Win32 `GetForegroundWindow()` P/Invoke, then uses `AutomationElement.FromHandle()` to get the root UI Automation element. Extracts the process name (via PID lookup) and window title, then calls `FindAll()` with conditions for `ControlType.Button`, `IsEnabled=true`, `IsOffscreen=false`. Builds an `ElementDescriptor` for each button found.

Returns a `ScanResult` containing the process name, window title, and a list of `(ElementDescriptor, AutomationElement)` tuples.

Handles all failure modes gracefully: null foreground window, process no longer exists, `ElementNotAvailableException`, `COMException`.

### Safety Filter (`Filtering/SafetyFilter.cs`)

Validates each candidate element through a strict pipeline:

1. Control type must be Button (`not_button`)
2. Element must be visible (`not_visible`)
3. Element must be enabled (`not_enabled`)
4. Button label must not contain any blocked word (`blocked_label`)
5. Process name must match a whitelist entry (`process_mismatch`)
6. Window title must match via TitleMatcher (`title_mismatch`)
7. Button label must match a whitelist label (`label_mismatch`)

Wildcard process entries (`"*"`) are skipped when `enableWildcardProcess` is false — they don't block other entries from matching.

Returns a `SafetyFilterResult` with pass/fail, the matched whitelist entry, and a specific rejection reason string.

### Button Prioritizer (`Filtering/ButtonPrioritizer.cs`)

When multiple buttons pass the safety filter in a single scan cycle, selects the single best candidate:

- Priority 0 (highest): button label exactly matches a whitelist label (case-insensitive)
- Priority 1 (lower): button label contains a whitelist label as a substring
- Tie-breaking: prefer the candidate matching the earliest button label in the whitelist order

This prevents accidental clicks (e.g., clicking "Cancel" when "Run Anyway" is the intended target).

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
  ├── Log startup info
  └── Enter scan loop:
        ├── await Task.Delay(scanIntervalMs)
        ├── Check kill switch → skip if disabled
        ├── Detector.Scan() → ScanResult (or null)
        ├── For each button:
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
│   ├── Detector.cs               # UI Automation foreground scanning
│   └── ScanResult.cs             # Scan result record
├── Filtering/
│   ├── ButtonPrioritizer.cs      # Multi-button priority selection
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
├── SafetyFilterTests.cs          # 15 tests
├── ButtonPrioritizerTests.cs     # 8 tests
├── DebounceTrackerTests.cs       # 14 tests
└── LoggerSetupTests.cs           # 8 tests
```
