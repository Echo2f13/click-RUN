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
- How often (in milliseconds) Click Run scans the foreground window. Values outside the range are clamped automatically with a warning log.

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
- When `true` (and `logLevel` is `"debug"`), logs detailed per-element information for every button scanned: process name, window title, button label, automation ID, pass/reject result, and specific rejection reason.

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
- Array of allowed button label strings. Case-insensitive exact match. The order matters for prioritization — earlier labels have higher priority when tie-breaking.

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

## File Locations

| File | Path |
|------|------|
| Config | `~/.clickrun/config.json` |
| Log | `~/.clickrun/clickrun.log` |
| Log (rotated) | `~/.clickrun/clickrun_001.log`, `_002.log`, `_003.log` |
