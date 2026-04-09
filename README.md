# Click Run

> Auto-click permission prompts in AI development tools. Stay in flow.

Click Run is an ultra-lightweight Windows background tool that automatically clicks "Run", "Allow", "Approve", "Continue", and other permission buttons in AI tools like **Kiro**, **VS Code** (Claude extension), and **Claude Desktop**.

No OCR. No mouse simulation. No screen scraping. Just the Windows UI Automation API doing what it does best — reading UI trees and invoking buttons programmatically.

## Why

AI tools interrupt your flow with permission prompts. Every. Single. Time.

- Kiro asks "Run this command?" — you click Run
- VS Code asks "Allow this action?" — you click Allow
- You step away for coffee — prompts pile up, nothing runs

Click Run eliminates this friction. It watches the foreground window, detects permission buttons, validates them against your whitelist, and clicks them. Safely. Deterministically. In the background.

## Quick Start

```bash
# Build
dotnet build src/ClickRun/ClickRun.csproj -c Release

# Run
dotnet run --project src/ClickRun/ClickRun.csproj -c Release
```

First run creates `~/.clickrun/config.json` with Kiro, VS Code, and Claude pre-configured.

## Safety First

Click Run has 7 layers of protection:

1. **Foreground only** — never touches background windows
2. **Process whitelist** — only clicks in apps you've approved
3. **Window title matching** — exact, contains, or regex
4. **Blocklist** — hard-rejects "Reject", "Cancel", "Deny" (configurable)
5. **Button label whitelist** — only clicks labels you've approved
6. **Button prioritization** — picks the safest match when multiple buttons exist
7. **Debounce** — prevents re-clicking the same button (2s cooldown)

Plus: **kill switch** (`Ctrl+Alt+R`) and **dry-run mode** for testing.

## Config

`~/.clickrun/config.json`:

```json
{
  "scanIntervalMs": 500,
  "dryRun": false,
  "blockedLabels": ["Reject", "Cancel", "Deny"],
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    }
  ]
}
```

See [docs/configuration.md](docs/configuration.md) for the full reference.

## Constraints

| Metric | Target |
|--------|--------|
| Memory | < 50 MB |
| CPU | < 1% (idle) |
| Scan interval | 300-800ms (default 500ms) |
| Dependencies | Serilog only |
| Platform | Windows 10+ |
| .NET | 8.0 |

## Docs

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | System design, components, data flow |
| [Configuration](docs/configuration.md) | Full config reference with examples |
| [Safety](docs/safety.md) | All 7 safety layers explained |
| [Troubleshooting](docs/troubleshooting.md) | Common issues and debugging |
| [API Reference](docs/api-reference.md) | Class and method documentation |
| [Contributing](docs/contributing.md) | How to contribute |

## How It Works

```
Every 500ms:
  → Get foreground window (Win32 API)
  → Scan all Button elements (UI Automation)
  → Filter: process ✓ title ✓ blocklist ✓ label ✓ visible ✓ enabled ✓
  → Prioritize: exact match > substring > whitelist order
  → Click via InvokePattern.Invoke() (with retry)
  → Debounce: record hash, prevent re-click for 2s
```

## License

[MIT](LICENSE)
