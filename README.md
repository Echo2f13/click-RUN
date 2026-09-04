# Click Run

> Auto-click permission prompts in AI development tools. Stay in flow.

Click Run is an ultra-lightweight Windows system tray application that automatically clicks "Run", "Allow", "Approve", "Accept", and other permission buttons in AI tools like **Kiro**, **VS Code agents**, and **Claude Desktop**.

**v2.0.0** adds first-class VS Code compatibility: both VS Code Stable (`Code`) and VS Code Insiders (`Code - Insiders`) are built-in default whitelist targets, with strict window-title scoping, keyboard fallback safety, and context-gated "Yes" handling for agent permission prompts.

No OCR. No mouse simulation. No screen scraping. Just the Windows UI Automation API reading UI trees and invoking buttons programmatically. For Electron/webview panels where UI Automation can't reach, a keyboard fallback sends numbered option keys.

## Install

Download `ClickRunSetup.exe` from [Releases](https://github.com/Echo2f13/click-RUN/releases/tag/v2.0.0) and run it. Click Run appears in your system tray immediately.

Or build from source:
```bash
dotnet publish src/ClickRun/ClickRun.csproj -c Release
```

## VS Code Compatibility (v2.0.0)

Click Run handles both VS Code flavors out of the box:

| Process name | Covers |
|---|---|
| `Code` | VS Code Stable |
| `Code - Insiders` | VS Code Insiders |

Both entries are included in the default `config.json` with the window-title pattern `"Visual Studio Code"` (`contains`), so clicks are scoped to actual VS Code windows and never fire in other applications that happen to use a `Code` process name.

**What gets clicked automatically:**
`Run`, `Allow`, `Approve`, `Continue`, `Accept`, `Accept command`, `Yes` (context-gated)

**Safety details specific to VS Code:**
- Window-title check (`Visual Studio Code`) is enforced on both the UI Automation path and the keyboard fallback path — an unmatched title is always rejected.
- `Yes` buttons require a safe-context keyword (e.g. "Permission", "Allow access") in the surrounding dialog text before being clicked. Dangerous-context keywords (e.g. "Delete", "Overwrite") cause a hard reject.
- VS Code agent prompts with keyboard hints in the label (e.g. `Allow (Ctrl+Enter)`) are handled via `prefixMatchLabels` — configure this if your prompts use dynamic suffixes.
- Keyboard fallback (for webview/Electron panels) applies the same title and context safety checks as the main UI Automation path.

**Recommended setup for VS Code:**
```json
{
  "enableKeyboardFallback": true,
  "whitelist": [
    {
      "processName": "Code",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command"]
    },
    {
      "processName": "Code - Insiders",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command"]
    }
  ]
}
```

Use `"dryRun": true` and `"enableDebugInstrumentation": true` to inspect what buttons Click Run detects before enabling live clicks.

## How It Works

Click Run lives in your system tray and scans for permission prompts every 500ms:

```
Every 500ms:
  → Get foreground window — or all whitelisted windows (multi-window mode)
  → Scan all Button elements (UI Automation)
  → Extract context text from dialog containers
  → Filter: blocklist ✓ process ✓ title ✓ label ✓ context ✓ visible ✓ enabled ✓
  → Prioritize safe execution actions over permanent trust actions
  → Click via InvokePattern.Invoke() (with retry)
  → Restore focus to previously active window (prevents focus stealing)
  → Fallback: send numbered key for Electron/webview panels
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

8 layers of protection:

1. **Window scope** — foreground only (default), or multi-window for whitelisted apps
2. **Process whitelist** — only clicks in apps you've approved
3. **Window title matching** — exact, contains, or regex
4. **Blocklist** — hard-rejects "Reject", "Cancel", "Deny"
5. **Button label whitelist** — only clicks labels you've approved
6. **Context-aware "Yes"** — "Yes" buttons only clicked when dialog context contains safe keywords; blocked when context contains dangerous keywords (Delete, Remove, etc.)
7. **Keyword priority** — Run > Accept > Trust > Yes; picks the safest match
8. **Debounce** — prevents re-clicking the same button (2s cooldown)

Plus: **kill switch** (`Ctrl+Alt+R`), **focus restoration** (prevents target apps from stealing focus), **dry-run mode**, **debug instrumentation**, and **single-instance guard**.

## Config

`~/.clickrun/config.json` (created automatically on first run):

```json
{
  "scanIntervalMs": 500,
  "multiWindowMode": false,
  "enableKeyboardFallback": false,
  "blockedLabels": ["Reject", "Cancel", "Deny"],
  "contextRequiredLabels": ["Yes"],
  "safeContextKeywords": ["Allow write", "Allow access", "Permission", "Grant", "Make this edit"],
  "dangerousContextKeywords": ["Delete", "Remove", "Overwrite", "Reset", "Drop"],
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command", "Trust", "Trust command and accept"]
    },
    {
      "processName": "Code",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command"]
    },
    {
      "processName": "Code - Insiders",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command"]
    }
  ]
}
```

See [docs/configuration.md](docs/configuration.md) for the full reference.

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
