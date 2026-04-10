# Click Run

An ultra-lightweight Windows system tray application that automatically clicks permission prompts (Run, Allow, Approve, Accept, Trust, Continue) in AI development tools like Kiro, VS Code, and Claude Desktop.

## The Problem

AI development tools constantly interrupt your flow with permission prompts:
- "Run this command?"
- "Allow this action?"
- "Trust command and accept?"

When you're deep in a coding session or away from your desk, these prompts pile up and block progress. Autopilot modes are inconsistent and still prompt. There's no unified solution across tools.

## The Solution

Click Run lives in your system tray and monitors windows using the Windows UI Automation API. It detects permission buttons that match your whitelist and clicks them programmatically. No OCR, no mouse simulation, no screen scraping — just deterministic, native API calls.

Two scanning modes:
- **Foreground only** (default): scans only the active window
- **Multi-window mode**: scans all visible windows belonging to whitelisted processes

## Install

### Option 1: Installer
Download `ClickRunSetup.exe` from Releases and run it. Installs to Program Files, adds Start Menu shortcut, and optionally starts on Windows login.

### Option 2: Build from source
```bash
dotnet build src/ClickRun/ClickRun.csproj -c Release
dotnet run --project src/ClickRun/ClickRun.csproj -c Release
```

### Option 3: Publish single executable
```bash
dotnet publish src/ClickRun/ClickRun.csproj -c Release
```
Output: `src/ClickRun/bin/Release/net8.0-windows/win-x64/publish/ClickRun.exe`

### Build the installer yourself
Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):
```bash
installer\build-installer.bat
```
Output: `installer\Output\ClickRunSetup.exe`

## System Tray

Click Run runs entirely in the system tray — no console window, no main window. On first run, it creates a default config at `~/.clickrun/config.json` with Kiro, VS Code, and Claude pre-configured.

Right-click the tray icon:
- **Running / Paused** — current status
- **Pause / Resume** — toggle scanning
- **Open Logs** — opens `~/.clickrun/` directory
- **Open Config** — opens `config.json` in your default editor
- **Start with Windows** — toggle auto-start via registry
- **Exit** — stop and close

Double-click the tray icon to toggle pause/resume.

## How It Works

Every 500ms (configurable):
1. Gets the foreground window — or enumerates all whitelisted windows (multi-window mode)
2. Scans all visible, enabled Button elements via UI Automation
3. Rejects buttons on the blocklist (Reject, Cancel, Deny)
4. Checks each button against the safety filter (process name, window title, button label)
5. Prioritizes by keyword order: Run > Allow > Approve > Accept > Trust
6. Clicks via `InvokePattern.Invoke()` (native API, not mouse simulation)
7. Records a debounce entry to prevent re-clicking the same button

## Safety

Click Run is designed to be safe by default:
- Only clicks buttons in whitelisted applications
- Only clicks buttons with whitelisted labels
- Blocklist prevents clicking dangerous buttons (Reject, Cancel, Deny)
- Keyword priority ensures the safest button is chosen
- Foreground window only by default — multi-window requires explicit opt-in
- Debounce prevents duplicate clicks (2s cooldown)
- Kill switch hotkey (Ctrl+Alt+R) instantly disables/enables
- Dry-run mode lets you validate before enabling real clicks
- Single instance — only one Click Run can run at a time

## Documentation

- [Architecture](architecture.md) — system design and component overview
- [Configuration](configuration.md) — full config reference with examples
- [Safety](safety.md) — safety mechanisms and how they work
- [Troubleshooting](troubleshooting.md) — common issues and debugging
- [API Reference](api-reference.md) — component and class documentation
- [Contributing](contributing.md) — how to contribute

## Requirements

- Windows 10 or later
- .NET 8 SDK (for building from source)
- < 50 MB RAM, < 1% CPU (idle scanning)

## License

[MIT](../LICENSE)
