# Click Run

> Auto-click permission prompts in AI development tools. Stay in flow.

Click Run is an ultra-lightweight Windows system tray application that automatically clicks "Run", "Allow", "Approve", "Accept", "Proceed", and other permission buttons in AI tools like **Kiro**, **VS Code agents**, **Claude Desktop**, and **Google Antigravity IDE**.

No OCR. No mouse simulation. No screen scraping. Just the Windows UI Automation API reading UI trees and invoking buttons programmatically. For Electron/webview panels where UI Automation can't reach, a keyboard fallback sends numbered option keys.

## Install

Download the latest `ClickRun.exe` from the [GitHub Releases page](https://github.com/Echo2f13/click-RUN/releases/latest) and run it — no installer required. The current release is **v2.5.0**. Click Run appears in your system tray immediately.

Or build from source:
```bash
dotnet publish src/ClickRun/ClickRun.csproj -c Release
```

## Supported Tools

| Process name | Covers |
|---|---|
| `Kiro` | Kiro IDE |
| `Code` | VS Code Stable |
| `Code - Insiders` | VS Code Insiders |
| `Claude` | Claude Desktop |
| `Antigravity IDE` | Antigravity IDE (VS Code-based editor) |
| `Antigravity` | Antigravity 2.0 (standalone Agent Manager) |
| `Antigravity IDE - Insiders` | Antigravity IDE Canary |

All entries are included in the default `config.json` with window-title scoping, so clicks never fire in unrelated applications.

## Antigravity IDE & Antigravity 2.0 (v2.5.0)

Click Run provides first-class support for Google Antigravity IDE and Antigravity 2.0.

**Important Setup:** Antigravity UI permission prompts run in Chromium webviews that are hidden from the Windows UI Automation tree by default. To make them visible, run this once in PowerShell and restart Antigravity:

```powershell
[System.Environment]::SetEnvironmentVariable('ELECTRON_FORCE_RENDERER_ACCESSIBILITY', '1', 'User')
```

**What gets clicked automatically:**
`Run`, `Allow`, `Approve`, `Continue`, `Proceed`, `Accept`, `Yes` (context-gated)

**Safety details specific to Antigravity:**
- Destructive shell commands (`rm -rf`, `del /f /s /q`, `format c:`) in the prompt context automatically block clicks, even for safe labels like `Run` or `Proceed`.
- Broad trust-escalation labels (`Always proceed`, `Trust workspace`) are excluded from the default whitelist — add them manually with context guards if needed.
- VS Code toolbar false-positives (`Run and Debug`, `Run Task`, `Run Without Debugging`, `Accept All Changes`) inherited by Antigravity IDE are on the default blocklist.

**Recommended setup for Antigravity:**
```json
{
  "enableKeyboardFallback": true,
  "whitelist": [
    {
      "processName": "Antigravity IDE",
      "windowTitles": [{ "pattern": "Antigravity IDE", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Proceed", "Accept", "Yes", "Accept command"]
    },
    {
      "processName": "Antigravity",
      "windowTitles": [{ "pattern": "Antigravity", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Proceed", "Accept", "Yes", "Accept command"]
    }
  ]
}
```

## VS Code Compatibility

Click Run handles both VS Code flavors out of the box. Both entries use the window-title pattern `"Visual Studio Code"` (`contains`) to scope clicks to actual VS Code windows.

**What gets clicked automatically:**
`Run`, `Allow`, `Approve`, `Continue`, `Accept`, `Accept command`, `Yes` (context-gated)

**Safety details specific to VS Code:**
- Window-title check (`Visual Studio Code`) is enforced on both the UI Automation path and the keyboard fallback path.
- `Yes` buttons require a safe-context keyword in the surrounding dialog text before being clicked. Dangerous-context keywords cause a hard reject.
- Labels with keyboard hints (e.g. `Allow (Ctrl+Enter)`) are handled via `prefixMatchLabels`.

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
  → Scan all Button/Hyperlink/ListItem elements (UI Automation)
  → Extract context text from dialog containers
  → Filter: blocklist ✓ process ✓ title ✓ label ✓ dangerous context ✓ visible ✓ enabled ✓
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
4. **Blocklist** — hard-rejects labels such as "Reject", "Cancel", "Deny", "Run and Debug", "Discard"
5. **Button label whitelist** — only clicks labels you've approved
6. **Dangerous context block** — hard-rejects any button when destructive keywords appear in the surrounding prompt text
7. **Context-aware confirmation** — ambiguous labels such as "Yes" require safe context keywords
8. **Debounce** — prevents re-clicking the same button (2s cooldown)

Plus: **kill switch** (`Ctrl+Alt+R`), **focus restoration** (prevents target apps from stealing focus), **dry-run mode**, **debug instrumentation**, and **single-instance guard**.

## Config

`~/.clickrun/config.json` (created automatically on first run):

```json
{
  "scanIntervalMs": 500,
  "multiWindowMode": false,
  "enableKeyboardFallback": false,
  "blockedLabels": ["Reject", "Cancel", "Deny", "Proceed without executing", "Discard",
                    "Run and Debug", "Run Task", "Run Without Debugging", "Accept All Changes"],
  "contextRequiredLabels": ["Yes"],
  "safeContextKeywords": ["Allow write", "Allow access", "Permission", "Grant", "Make this edit",
                          "Allow access to", "Allow network request", "tool execution"],
  "dangerousContextKeywords": ["Overwrite", "Reset", "Drop", "Erase", "Destroy",
                               "rm -rf", "del /f /s /q", "format c:"],
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
    },
    {
      "processName": "Claude",
      "windowTitles": [{ "pattern": "Claude", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command"]
    },
    {
      "processName": "Antigravity IDE",
      "windowTitles": [{ "pattern": "Antigravity IDE", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Proceed", "Accept", "Yes", "Yes, allow all edits this session", "Accept command"]
    },
    {
      "processName": "Antigravity",
      "windowTitles": [{ "pattern": "Antigravity", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Proceed", "Accept", "Yes", "Accept command"]
    },
    {
      "processName": "Antigravity IDE - Insiders",
      "windowTitles": [{ "pattern": "Antigravity IDE - Insiders", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Proceed", "Accept", "Yes", "Yes, allow all edits this session", "Accept command"]
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
| .NET | 8.0 (bundled — no install required) |
| Memory | < 50 MB |
| CPU | < 1% (idle) |

## License

[MIT](LICENSE)
