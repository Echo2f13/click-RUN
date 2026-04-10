# Troubleshooting

## Click Run isn't clicking buttons

### Step 1: Enable debug instrumentation
Set these in `~/.clickrun/config.json`:
```json
{
  "logLevel": "debug",
  "enableDebugInstrumentation": true
}
```

Restart Click Run and check `~/.clickrun/clickrun.log` for per-element details.

### Step 2: Check rejection reasons

Each rejected element logs a specific reason:

| Reason | Meaning | Fix |
|--------|---------|-----|
| `process_mismatch` | Process name doesn't match any whitelist entry | Add the correct process name to whitelist. Check Task Manager for the exact process name. |
| `title_mismatch` | Window title doesn't match any pattern | Update `windowTitles` patterns. Try `"matchMode": "contains"` for partial matching. |
| `label_mismatch` | Button label doesn't match any whitelist label | Add the exact button label to `buttonLabels`. Check the log for the actual label text. |
| `blocked_label` | Button label contains a blocked word | The button is intentionally blocked. Remove the word from `blockedLabels` only if you're sure it's safe. |
| `not_button` | Element is not a Button control type | Not a clickable button — this is expected for non-button UI elements. |
| `not_visible` | Element is offscreen/hidden | The button exists but isn't visible. Usually transient. |
| `not_enabled` | Element is disabled | The button exists but is grayed out. Usually transient. |
| `debounce_cooldown` | Same button was clicked recently | Wait for the cooldown (default 2s) or reduce `debounceCooldownMs`. |

### Step 3: Use dry run mode
Set `"dryRun": true` to test without actually clicking. Look for `[DRY RUN] Would click:` entries in the log.

## Background window prompts not detected

Click Run defaults to foreground-only scanning. To detect prompts in background windows:

```json
{
  "multiWindowMode": true
}
```

When enabled, check the log for multi-window diagnostics:
```
MultiWindow: EnumWindows found 29 visible windows
MultiWindow: Found window — Process=Kiro | Title=project-a - Kiro
MultiWindow: Found window — Process=Kiro | Title=project-b - Kiro
MultiWindow: 2 whitelisted windows found, 2 with buttons
```

If your background window isn't listed:
- Verify the process name matches your whitelist (check Task Manager)
- The window must be visible (not minimized)
- The window must have a non-empty title

## Wrong button clicked (e.g., Trust instead of Accept)

Click Run uses strict keyword priority based on the `buttonLabels` order in your config. Earlier labels have higher priority. Default order:

```
Run(0) > Allow(1) > Approve(2) > Continue(3) > Yes(4) > Accept(5) > Trust(7)
```

If "Trust command and accept" is being clicked instead of a "Run" button, ensure "Run" appears before "Trust" in your `buttonLabels` array.

## Kill switch hotkey doesn't work

Error in log: `Failed to register global hotkey (Win32 error 1409)`

This means the hotkey combination is already registered by another application. Change `killSwitchHotkey` in config to a different combination:
```json
{
  "killSwitchHotkey": "Ctrl+Alt+K"
}
```

## Log files are too large

Click Run rotates logs automatically at 10 MB, keeping 3 files. If debug instrumentation is enabled, logs grow fast. For production use:
```json
{
  "logLevel": "info",
  "enableDebugInstrumentation": false
}
```

Multi-window mode with debug instrumentation generates significantly more log output since every button in every whitelisted window is logged each cycle.

## Config parse error on startup

Click Run exits with a descriptive error including the line number. Common issues:
- Trailing commas in JSON (allowed — Click Run supports them)
- Missing quotes around strings
- Invalid regex pattern in a `"matchMode": "regex"` entry

## Build errors: UIAutomation not found

The project requires `net8.0-windows` target framework and the `Microsoft.WindowsDesktop.App` framework reference. Verify `ClickRun.csproj` contains:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
```
and:
```xml
<FrameworkReference Include="Microsoft.WindowsDesktop.App" />
```

## Finding the correct process name

Open Task Manager, find your application, right-click → "Go to details". The "Name" column in the Details tab shows the process name (without `.exe`). That's what goes in `processName`.

Common process names:
| Application | Process Name |
|-------------|-------------|
| Kiro | `Kiro` |
| VS Code | `Code` |
| Claude Desktop | `Claude` |

## Finding the correct button label

Enable debug instrumentation and check the log. Each scanned button shows its exact label:
```
Element: Process=Kiro | Window=... | Label=Accept command | AutomationId= | Result=REJECT | Reason=label_mismatch
```

The `Label` value is what you need to add to `buttonLabels`.
