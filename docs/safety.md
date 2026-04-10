# Safety Mechanisms

Click Run is designed with safety as the top priority. Every layer of the system includes safeguards to prevent unintended clicks.

## Defense in Depth

Click Run uses 7 layers of protection before any click is executed:

### Layer 1: Window Scope Control
By default, Click Run only scans the currently active (foreground) window. Background windows are completely ignored.

With `multiWindowMode` enabled, Click Run scans all visible windows — but only those belonging to whitelisted processes. Unknown applications are never scanned, even in multi-window mode. Empty/helper windows (no title) are skipped.

### Layer 2: Process Name Whitelist
Only applications explicitly listed in the whitelist are considered. The process name must match exactly (case-insensitive). Unknown applications are ignored completely.

Wildcard process matching (`"*"`) is disabled by default and requires an explicit `enableWildcardProcess: true` flag.

### Layer 3: Window Title Matching
The window title must match at least one configured pattern for the matched process. Three match modes are available (exact, contains, regex), with `exact` as the default for maximum precision.

### Layer 4: Blocklist (Hard Reject)
Before any whitelist matching, button labels are checked against the blocklist. Any button whose label contains a blocked word (case-insensitive substring match) is immediately rejected. Default blocked words: `Reject`, `Cancel`, `Deny`.

The blocklist runs before the whitelist, so even if a button label like "Cancel and Retry" contains "Retry" which might match a whitelist entry, the "Cancel" blocklist entry rejects it first.

### Layer 5: Button Label Whitelist
Only buttons whose labels exactly match (case-insensitive) a whitelist entry are accepted. Substring matching is used only for prioritization, not for initial acceptance by the safety filter.

### Layer 6: Keyword Priority
When multiple buttons pass all filters, the prioritizer selects the single safest choice using strict keyword priority defined by the whitelist label order:

- `Run` (index 0) — highest priority
- `Allow` (index 1)
- `Approve` (index 2)
- `Continue` (index 3)
- `Yes` (index 4)
- `Accept` (index 5)
- `Trust` (index 7)

The label index is the primary ranking. Match type (exact vs substring) is secondary. This means "Trust command and accept" resolves to the "Accept" keyword (index 5) rather than "Trust" (index 7), because Accept has higher priority.

Only one button is clicked per scan cycle, even in multi-window mode across multiple windows.

### Layer 7: Debounce Protection
After clicking a button, a 2-second cooldown prevents re-clicking the same element. Elements are identified by a SHA256 hash of their process name, window title, button label, and automation ID.

## Kill Switch

Press `Ctrl+Alt+R` (configurable) to instantly disable Click Run. Press again to re-enable. State changes are logged at info level. When disabled, no scanning occurs at all.

## Dry Run Mode

Set `"dryRun": true` in config to run Click Run in simulation mode. All scanning, filtering, and prioritization proceed normally, but no actual clicks are performed. Instead, Click Run logs what it would have clicked:

```
[INF] [DRY RUN] Would click: Kiro | myfile.ts - Kiro | Run
```

Debounce entries are still recorded to accurately simulate real timing behavior. Use this to validate your whitelist configuration before enabling real clicks.

## Debug Instrumentation

Set `"enableDebugInstrumentation": true` with `"logLevel": "debug"` to see per-element details:

```
Element: Process=Kiro | Window=myfile.ts - Kiro | Label=Cancel | AutomationId= | Result=REJECT | Reason=blocked_label
Element: Process=Kiro | Window=myfile.ts - Kiro | Label=Run | AutomationId= | Result=PASS | Reason=
```

In multi-window mode, additional diagnostics show window enumeration:
```
MultiWindow: EnumWindows found 29 visible windows
MultiWindow: Found window — Process=Kiro | Title=project-a - Kiro | Handle=657620
MultiWindow: Found window — Process=Kiro | Title=project-b - Kiro | Handle=263606
MultiWindow: 2 whitelisted windows found, 2 with buttons
```

## What Click Run Never Does

- Never simulates mouse movement or keyboard input
- Never interacts with applications not in the whitelist
- Never clicks buttons containing blocked words (Reject, Cancel, Deny)
- Never clicks more than one button per scan cycle
- Never re-clicks the same button within the debounce cooldown
- Never runs with wildcard process matching unless explicitly enabled
- Never scans non-whitelisted processes even in multi-window mode
