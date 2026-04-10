# Architecture

## Overview

Click Run is a Windows system tray application built with C# (.NET 8) and WinForms. It runs as a `WinExe` with no console window, hosting a `NotifyIcon` in the system tray. The core scan-and-click engine runs on a background thread while the WinForms message loop handles the tray UI and global hotkey.

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Click Run (WinExe)                       │
│                                                              │
│  ┌──────────────┐                                            │
│  │  Program.cs   │ [STAThread] entry point                   │
│  │  SingleInstance│ Named Mutex guard                         │
│  └──────┬───────┘                                            │
│         ▼                                                    │
│  ┌──────────────┐     ┌──────────────────────────────────┐  │
│  │   TrayApp     │────▶│       ClickRunEngine              │  │
│  │  (NotifyIcon) │     │  (background Task)                │  │
│  │  Context Menu │     │                                    │  │
│  │  - Status     │     │  ┌──────────┐  ┌──────────────┐  │  │
│  │  - Pause      │     │  │ Detector  │─▶│ Safety Filter │  │  │
│  │  - Open Logs  │     │  └────┬─────┘  └──────┬───────┘  │  │
│  │  - Open Config│     │       │               │           │  │
│  │  - Auto Start │     │       ▼               ▼           │  │
│  │  - Exit       │     │  ┌─────────┐  ┌──────────────┐   │  │
│  └──────────────┘     │  │ UI Auto  │  │ Prioritizer  │   │  │
│                        │  │  API     │  └──────┬───────┘   │  │
│                        │  └─────────┘         │           │  │
│                        │                       ▼           │  │
│                        │  ┌──────────────────────────┐     │  │
│                        │  │ Clicker (w/Retry)        │     │  │
│                        │  └──────────────────────────┘     │  │
│                        │                                    │  │
│                        │  ┌────────────┐ ┌──────────────┐  │  │
│                        │  │ Debounce   │ │ Kill Switch  │  │  │
│                        │  │ Tracker    │ │ (Hotkey)     │  │  │
│                        │  └────────────┘ └──────────────┘  │  │
│                        └──────────────────────────────────┘  │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐     │
│  │ Config Parser │  │ Title Matcher│  │ Logger        │     │
│  │ / Serializer  │  │              │  │ (Serilog)     │     │
│  └──────────────┘  └──────────────┘  └───────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

## Application Lifecycle

1. `Program.Main()` — `[STAThread]`, acquires named Mutex for single-instance
2. Loads config from `~/.clickrun/config.json` (creates default if missing)
3. Initializes Serilog logger
4. Creates `TrayApp` (WinForms `ApplicationContext`) with `NotifyIcon`
5. `TrayApp` creates `ClickRunEngine` and calls `Start()`
6. Engine runs scan loop on background `Task`
7. `Application.Run(trayApp)` — WinForms message loop handles tray UI + hotkey
8. On Exit: engine stops, hotkey unregistered, logs flushed

## Components

### Program.cs (Entry Point)
- `[STAThread]` for WinForms compatibility
- `SingleInstance` — named Mutex (`Global\ClickRun_SingleInstance_Mutex`) prevents duplicate launches
- Config errors shown via `MessageBox` (no console available)

### TrayApp (`Tray/TrayApp.cs`)
- Extends `ApplicationContext` — no visible window
- `NotifyIcon` with custom icon (falls back to `SystemIcons.Application`)
- Context menu: status, pause/resume, open logs, open config, auto-start toggle, exit
- Double-click toggles pause/resume
- Auto-start via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key

### ClickRunEngine (`Engine/ClickRunEngine.cs`)
- Extracted core scan loop — owns all components (Detector, SafetyFilter, Prioritizer, Clicker, DebounceTracker, KillSwitch)
- `Start()` / `Stop()` / `Pause()` / `Resume()` / `TogglePause()`
- Runs on background `Task` with `CancellationToken`
- `IDisposable` — cleans up KillSwitch on dispose

### Detector (`Detection/Detector.cs`)
Two scanning modes:
- `Scan()` — foreground window only via `GetForegroundWindow()`
- `ScanAll()` — all visible whitelisted windows via `EnumWindows`

### Safety Filter (`Filtering/SafetyFilter.cs`)
Pipeline: Button type → Visible → Enabled → Blocklist → Process → Title → Label

### Button Prioritizer (`Filtering/ButtonPrioritizer.cs`)
Keyword priority by whitelist label index. Run(0) > Allow(1) > Accept(5) > Trust(7).

### Clicker (`Clicking/Clicker.cs`)
`InvokePattern.Invoke()` with optional pre-click delay and single retry.

### Other Components
- `TitleMatcher` — exact/contains/regex window title matching
- `DebounceTracker` — SHA256 hash-based duplicate click prevention
- `KillSwitch` — Win32 `RegisterHotKey` on dedicated message-loop thread
- `ConfigParser` / `ConfigSerializer` / `DefaultConfig` — JSON config management
- `LoggerSetup` — Serilog file sink with rotation

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Language | C# (.NET 8) |
| App Type | WinExe (WinForms tray app) |
| UI Automation | System.Windows.Automation |
| Window Enumeration | Win32 EnumWindows, IsWindowVisible, GetWindowThreadProcessId |
| Hotkey | Win32 RegisterHotKey via P/Invoke |
| Tray | WinForms NotifyIcon + ContextMenuStrip |
| Auto-Start | Windows Registry (HKCU\...\Run) |
| Single Instance | Named Mutex |
| Config | System.Text.Json |
| Logging | Serilog + Serilog.Sinks.File |
| Installer | Inno Setup 6 |
| Distribution | Self-contained single executable (win-x64) |

## Project Structure

```
src/ClickRun/
├── ClickRun.csproj
├── Program.cs                    # [STAThread] entry, single-instance, tray launch
├── Clicking/
│   ├── Clicker.cs                # InvokePattern click with retry
│   └── ClickResult.cs
├── Config/
│   ├── ConfigParser.cs           # JSON loading and validation
│   ├── ConfigSerializer.cs       # JSON serialization
│   └── DefaultConfig.cs          # Default config creation
├── Detection/
│   ├── Detector.cs               # UI Automation scanning (foreground + multi-window)
│   └── ScanResult.cs
├── Engine/
│   └── ClickRunEngine.cs         # Core scan loop (start/stop/pause/resume)
├── Filtering/
│   ├── ButtonPrioritizer.cs      # Keyword-priority button selection
│   ├── SafetyFilter.cs           # Whitelist + blocklist validation
│   └── SafetyFilterResult.cs
├── Hotkey/
│   └── KillSwitch.cs             # Global hotkey toggle
├── Logging/
│   └── LoggerSetup.cs            # Serilog configuration
├── Matching/
│   └── TitleMatcher.cs           # Window title matching (exact/contains/regex)
├── Models/
│   ├── Candidate.cs
│   ├── Configuration.cs
│   ├── ElementDescriptor.cs
│   ├── MatchMode.cs
│   ├── WhitelistEntry.cs
│   └── WindowTitlePattern.cs
├── Tracking/
│   └── DebounceTracker.cs        # Hash-based debounce tracking
└── Tray/
    ├── SingleInstance.cs          # Named Mutex single-instance guard
    └── TrayApp.cs                # NotifyIcon tray application

installer/
├── clickrun-setup.iss            # Inno Setup script
├── build-installer.bat           # One-click build script
└── Output/
    └── ClickRunSetup.exe         # Built installer

tests/ClickRun.Tests/
├── ClickRun.Tests.csproj
├── SafetyFilterTests.cs
├── ButtonPrioritizerTests.cs
├── DebounceTrackerTests.cs
└── LoggerSetupTests.cs
```
