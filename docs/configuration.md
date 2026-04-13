# Configuration Reference

Click Run is configured via a JSON file at `~/.clickrun/config.json`. On first run, a default config is created automatically.

## Full Schema

```json
{
  "scanIntervalMs": 500,
  "debounceCooldownMs": 2000,
  "killSwitchHotkey": "Ctrl+Alt+R",
  "logLevel": "info",
  "enableWildcardProcess": false,
  "enableDebugInstrumentation": false,
  "dryRun": false,
  "preClickDelayMs": 0,
  "blockedLabels": ["Reject", "Cancel", "Deny"],
  "contextRequiredLabels": ["Yes"],
  "safeContextKeywords": ["Allow write", "Allow access", "Permission", "Grant", "Make this edit", "apply edit", "run command", "execute"],
  "dangerousContextKeywords": ["Delete", "Remove", "Overwrite", "Reset", "Drop", "Erase", "Destroy"],
  "multiWindowMode": false,
  "enableKeyboardFallback": false,
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [
        { "pattern": "Kiro", "matchMode": "contains" }
      ],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command", "Trust", "Trust command and accept"]
    },
    {
      "processName": "Code",
      "windowTitles": [
        { "pattern": "Visual Studio Code", "matchMode": "contains" }
      ],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    },
    {
      "processName": "Claude",
      "windowTitles": [
        { "pattern": "Claude", "matchMode": "contains" }
      ],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    }
  ]
}
```

## Options

### `scanIntervalMs`
- Type: `int`
- Default: `500`
- Valid range: `300` – `800`
- How often (in milliseconds) Click Run scans for permission prompts. Values outside the range are clamped automatically with a warning log.

### `debounceCooldownMs`
- Type: `int`
- Default: `2000`
- Minimum time (in milliseconds) before the same button can be clicked again. Prevents rapid duplicate clicks.

### `killSwitchHotkey`
- Type: `string`
- Default: `"Ctrl+Alt+R"`
- Global hotkey to toggle Click Run on/off. Format: modifier keys joined with `+`, followed by the key name. Supported modifiers: `Ctrl`, `Alt`, `Shift`, `Win`. Supported keys: `A`-`Z`, `0`-`9`, `F1`-`F24`, `Space`, `Enter`, `Escape`, etc.

### `logLevel`
- Type: `string`
- Default: `"info"`
- Options: `"debug"`, `"info"`, `"warn"`, `"error"`
- Controls the minimum log level written to `~/.clickrun/clickrun.log`.

### `enableWildcardProcess`
- Type: `bool`
- Default: `false`
- When `false`, any whitelist entry with `processName: "*"` is ignored. Must be explicitly set to `true` to allow wildcard matching. This prevents accidentally clicking buttons in unknown applications.

### `enableDebugInstrumentation`
- Type: `bool`
- Default: `false`
- When `true` (and `logLevel` is `"debug"`), logs detailed per-element information for every button scanned: process name, window title, button label, automation ID, pass/reject result, and specific rejection reason. In multi-window mode, also logs window enumeration diagnostics.

### `dryRun`
- Type: `bool`
- Default: `false`
- When `true`, Click Run performs all scanning, filtering, and prioritization but does NOT actually click. Logs `[DRY RUN] Would click: ...` at info level. Debounce entries are still recorded to simulate real behavior. Use this to validate your config before enabling real clicks.

### `preClickDelayMs`
- Type: `int`
- Default: `0`
- Valid range: `0` – `200`
- Milliseconds to wait before clicking a detected button. Helps prevent race conditions with UI rendering in some applications. Set to `0` for no delay.

### `blockedLabels`
- Type: `string[]`
- Default: `["Reject", "Cancel", "Deny"]`
- Button labels that are always rejected, even if they match a whitelist entry. Uses case-insensitive substring matching — a button labeled "Reject changes" would be blocked by the "Reject" entry. This is a hard safety constraint that runs before whitelist matching.

### `multiWindowMode`
- Type: `bool`
- Default: `false`
- When `false` (default), Click Run only scans the foreground (active) window. Background windows with permission prompts are ignored.
- When `true`, Click Run uses Win32 `EnumWindows` to find all visible windows, filters to only whitelisted process names, and scans each matching window. This catches permission prompts in background windows (e.g., Kiro has a prompt but VS Code is in the foreground). Windows with empty titles (helper/tooltip windows) are skipped. All other safety rules (blocklist, label matching, debounce) still apply. Only one button is clicked per cycle across all windows.

### `enableKeyboardFallback`
- Type: `bool`
- Default: `false`
- When `true`, if UI Automation finds no clickable candidates in a scan cycle, Click Run checks the extracted context text for numbered option patterns (e.g., "1 Yes", "2 No") and sends the corresponding key press. This handles Electron/webview panels where buttons aren't accessible via UI Automation. The fallback applies all safety checks (whitelist, blocklist, dangerous context) before sending any key. The target window is focused via `SetForegroundWindow` before input.

### `contextRequiredLabels`
- Type: `string[]`
- Default: `["Yes"]`
- Button labels that require context validation before clicking. When a button matches one of these labels, Click Run checks the dialog context text (extracted from the UI tree around the button + window title) for safe and dangerous keywords. If the context contains a dangerous keyword, the click is rejected. If no safe keyword is found, the click is also rejected. Labels not in this list (like "Run", "Accept") are clicked without context checks.

### `safeContextKeywords`
- Type: `string[]`
- Default: `["Allow write", "Allow access", "Permission", "Grant", "Allow edit", "Allow all", "Make this edit", "apply edit", "run command", "execute"]`
- Keywords that must appear in the dialog context for context-required labels (like "Yes") to be clicked. Case-insensitive substring match against the combined window title + extracted UI context text.

### `dangerousContextKeywords`
- Type: `string[]`
- Default: `["Delete", "Remove", "Overwrite", "Reset", "Drop", "Erase", "Destroy"]`
- Keywords that cause immediate rejection of context-required labels. Checked before safe keywords — if both a safe and dangerous keyword are present, the click is rejected. Case-insensitive substring match.

### `whitelist`
- Type: `WhitelistEntry[]`
- Each entry specifies a target application:

#### `whitelist[].processName`
- The Windows process name to match (e.g., `"Kiro"`, `"Code"`, `"Claude"`). Case-insensitive. Use `"*"` for wildcard (requires `enableWildcardProcess: true`).

#### `whitelist[].windowTitles`
- Array of window title patterns. At least one must match.
- Each entry has:
  - `pattern`: the string to match against
  - `matchMode`: `"exact"` (default), `"contains"`, or `"regex"`

#### `whitelist[].buttonLabels`
- Array of allowed button label strings. Case-insensitive exact match. The order defines keyword priority — earlier labels have higher priority. When multiple buttons match, the one matching the earliest label wins.

## Keyword Priority

The `buttonLabels` order is critical. It defines which button gets clicked when multiple pass the filter:

```
Index 0: "Run"                      ← highest priority
Index 1: "Allow"
Index 2: "Approve"
Index 3: "Continue"
Index 4: "Yes"
Index 5: "Accept"
Index 6: "Accept command"
Index 7: "Trust"
Index 8: "Trust command and accept" ← lowest priority
```

Examples:
- If both "Run" and "Accept command" pass → "Run" wins (index 0 < index 6)
- "Trust command and accept" resolves to "Accept" keyword (index 5) not "Trust command and accept" (index 8)
- "Run anyway" resolves to "Run" keyword (index 0)

## Window Title Match Modes

| Mode | Behavior | Example |
|------|----------|---------|
| `exact` | Full title must equal pattern (case-insensitive) | `"Kiro"` matches only `"Kiro"` |
| `contains` | Title must contain pattern as substring | `"Kiro"` matches `"myfile.ts - Kiro"` |
| `regex` | Title must match the regex pattern | `"Kiro\|VS Code"` matches either |

Regex patterns are validated at startup. Invalid patterns cause Click Run to exit with an error.

## Example Configs

### Minimal (Kiro only)
```json
{
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Continue"]
    }
  ]
}
```

### Testing mode (dry run + debug)
```json
{
  "logLevel": "debug",
  "enableDebugInstrumentation": true,
  "dryRun": true,
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    }
  ]
}
```

### Multi-window mode (scan all whitelisted apps)
```json
{
  "multiWindowMode": true,
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command", "Trust", "Trust command and accept"]
    },
    {
      "processName": "Code",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes"]
    }
  ]
}
```

### Production mode (minimal logging, multi-window)
```json
{
  "logLevel": "info",
  "enableDebugInstrumentation": false,
  "multiWindowMode": true,
  "preClickDelayMs": 50,
  "whitelist": [
    {
      "processName": "Kiro",
      "windowTitles": [{ "pattern": "Kiro", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Accept command", "Trust", "Trust command and accept"]
    },
    {
      "processName": "Code",
      "windowTitles": [{ "pattern": "Visual Studio Code", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    },
    {
      "processName": "Claude",
      "windowTitles": [{ "pattern": "Claude", "matchMode": "contains" }],
      "buttonLabels": ["Run", "Allow", "Approve", "Continue", "Yes", "Accept", "Trust"]
    }
  ]
}
```

## File Locations

| File | Path |
|------|------|
| Config | `~/.clickrun/config.json` |
| Log | `~/.clickrun/clickrun.log` |
| Log (rotated) | `~/.clickrun/clickrun_001.log`, `_002.log`, `_003.log` |
