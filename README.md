# Click Run

> Auto-click permission prompts in AI development tools. Stay in flow.

Click Run is an ultra-lightweight Windows system tray application that automatically clicks "Run", "Allow", "Approve", "Accept", "Trust", and other permission buttons in AI tools like **Kiro**, **VS Code** (Claude extension), and **Claude Desktop**.

No OCR. No mouse simulation. No screen scraping. Just the Windows UI Automation API reading UI trees and invoking buttons programmatically.

## Install

Download `ClickRunSetup.exe` from [Releases](https://github.com/click-run/click-run/releases) and run it. Click Run appears in your system tray immediately.

Or build from source:
```bash
dotnet publish src/ClickRun/ClickRun.csproj -c Release
```

## How It Works

Click Run lives in your system tray and scans for permission prompts every 500ms:

```
Every 500ms:
  → Get foreground window — or all whitelisted windows (multi-window mode)
  → Scan all Button elements (UI Automation)
  → Filter: blocklist ✓ process ✓ title ✓ label ✓ visible ✓ enabled ✓
  → Prioritize by keyword order: Run > Allow > Accept > Trust
  → Click via InvokePattern.Invoke() (with retry)
  → Debounce: record hash, prevent re-click for 2s
```

## System Tray

Right-click the tray icon for:
- **Running / Paused** — current status
- **Pause / Resume** — toggle scanning (or double-click the icon)
- **Open Logs** — opens the log directory
- **Open Config** — opens config.json in your default editor
- **Start with Windows** — toggle auto-start on login
- **Exit** — stop and close

## Safety

7 layers of protection:

1. **Window scope** — foreground only (default), or multi-window for whitelisted apps
2. **Process whitelist** — only clicks in apps you've approved
3. **Window title matching** — exact, contains, or regex
4. **Blocklist** — hard-rejects "Reject", "Cancel", "Deny"
5. **Button label whitelist** — only clicks labels you've approved
6. **Keyword priority** — Run > Accept > Trust; picks the safest match
7. **Debounce** — prevents re-clicking the same button (2s cooldown)

Plus: **kill switch** (`Ctrl+Alt+R`), **dry-run mode**, and **debug instrumentation**.

## Config

`~/.clickrun/config.json` (created automatically on first run):

```json
{
  "scanIntervalMs": 500,
  "multiWindowMode": false,
  "blockedLabels": ["Reject", "Cancel", "Deny"],
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command", "Trust", "Trust command and accept"]
    }
  ]
}
```

See [docs/configuration.md](docs/configuration.md) for the full reference.

## Multi-Window Mode

Set `"multiWindowMode": true` to scan all visible windows belonging to whitelisted processes. Catches prompts in background Kiro/VS Code windows while you work in another app.

## Docs

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | System design, components, data flow |
| [Configuration](docs/configuration.md) | Full config reference with examples |
| [Safety](docs/safety.md) | All safety layers explained |
| [Troubleshooting](docs/troubleshooting.md) | Common issues and debugging |
| [API Reference](docs/api-reference.md) | Class and method documentation |
| [Contributing](docs/contributing.md) | How to contribute |

## Requirements

| Metric | Value |
|--------|-------|
| Platform | Windows 10+ |
| .NET | 8.0 |
| Memory | < 50 MB |
| CPU | < 1% (idle) |

## License

[MIT](LICENSE)
