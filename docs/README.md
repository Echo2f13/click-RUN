# Click Run

An ultra-lightweight Windows background tool that automatically clicks permission prompts (Run, Allow, Approve, Continue) in AI development tools like Kiro, VS Code, and Claude Desktop.

## The Problem

AI development tools constantly interrupt your flow with permission prompts:
- "Run this command?"
- "Allow this action?"
- "Trust this operation?"

When you're deep in a coding session or away from your desk, these prompts pile up and block progress. Autopilot modes are inconsistent and still prompt. There's no unified solution across tools.

## The Solution

Click Run monitors the foreground window using the Windows UI Automation API, detects permission buttons that match your whitelist, and clicks them programmatically. No OCR, no mouse simulation, no screen scraping — just deterministic, native API calls.

## Quick Start

### Prerequisites
- Windows 10 or later
- .NET 8 SDK

### Build
```bash
cd src/ClickRun
dotnet build -c Release
```

### Run
```bash
dotnet run --project src/ClickRun/ClickRun.csproj -c Release
```

On first run, Click Run creates a default config at `~/.clickrun/config.json` with Kiro, VS Code, and Claude pre-configured.

### Publish (single executable)
```bash
dotnet publish src/ClickRun/ClickRun.csproj -c Release
```

Output: `src/ClickRun/bin/Release/net8.0-windows/win-x64/publish/ClickRun.exe`

## How It Works

Every 500ms (configurable), Click Run:
1. Gets the foreground window via `GetForegroundWindow()`
2. Scans all visible, enabled Button elements via UI Automation
3. Checks each button against the safety filter (process name, window title, button label, blocklist)
4. Prioritizes the best match if multiple buttons pass
5. Clicks via `InvokePattern.Invoke()` (native API, not mouse simulation)
6. Records a debounce entry to prevent re-clicking the same button

## Safety

Click Run is designed to be safe by default:
- Only clicks buttons in whitelisted applications
- Only clicks buttons with whitelisted labels
- Blocklist prevents clicking dangerous buttons (Reject, Cancel, Deny) even if they match a substring
- Foreground window only — never clicks background windows
- Debounce prevents duplicate clicks (2s cooldown)
- Kill switch hotkey (Ctrl+Alt+R) instantly disables/enables
- Dry-run mode lets you validate before enabling real clicks

## Configuration

Config file: `~/.clickrun/config.json`

See [docs/configuration.md](configuration.md) for the full reference.

## Documentation

- [Architecture](architecture.md) — system design and component overview
- [Configuration](configuration.md) — full config reference with examples
- [Safety](safety.md) — safety mechanisms and how they work
- [Troubleshooting](troubleshooting.md) — common issues and debugging
- [API Reference](api-reference.md) — component and class documentation
- [Contributing](contributing.md) — how to contribute

## Constraints

- < 50 MB RAM
- < 1% CPU (idle scanning)
- No GUI
- Single executable
- Windows only (MVP)

## License

[MIT](../LICENSE)
