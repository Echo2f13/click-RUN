# Contributing

Thanks for your interest in contributing to Click Run.

## Development Setup

### Prerequisites
- Windows 10 or later
- .NET 8 SDK
- An editor with C# support (VS Code, Rider, Visual Studio)

### Build
```bash
dotnet build src/ClickRun/ClickRun.csproj
```

For the test-friendly Debug configuration:
```bash
dotnet build tests/ClickRun.Tests/ClickRun.Tests.csproj -c Debug
```

### Run Tests
```bash
dotnet test tests/ClickRun.Tests/ClickRun.Tests.csproj
```

To run the full suite after building without rebuilding:
```bash
dotnet test tests/ClickRun.Tests/ClickRun.Tests.csproj -c Debug --logger "console;verbosity=normal" --no-build
```

### Run
```bash
dotnet run --project src/ClickRun/ClickRun.csproj
```

The app appears in the system tray (no console window).

### Build Installer
Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):
```bash
installer\build-installer.bat
```
Output: `installer\Output\ClickRunSetup.exe`

## Test Suite

The suite contains 363 tests across 11 test files, including VS Code compatibility, Antigravity compatibility, trust fallback integration, adversarial, and property-based coverage. The current verified baseline is 363 passed, 0 failed, and 0 skipped.

- `SafetyFilterTests.cs` — whitelist matching, blocklist, wildcard safety, rejection reasons
- `ButtonPrioritizerTests.cs` — intent priority and multi-candidate selection
- `DebounceTrackerTests.cs` — hash computation, cooldown, pruning, and collision resistance
- `TrustFallback*Tests.cs` — trust-dialog detection, safety checks, integration, adversarial, and property tests
- `VsCodeCompatibilityTests.cs` — VS Code Stable/Insiders labels, title matching, and keyboard fallback context gates
- `AntigravityCompatibilityTests.cs` — Antigravity IDE/2.0 labels, title matching, false-positive guards, dangerous context blocks, dynamic suffix handling, and keyboard fallback
- `LoggerSetupTests.cs` — log level parsing and logger creation

## Validation

Click Run is a Windows tray application and must be validated on Windows. Before enabling live clicks, use a config with `dryRun: true`, debug logging enabled, and only the intended process/window-title entries whitelisted. Confirm a real agent prompt produces `Result=PASS` and `[DRY RUN] Would click` entries in `%USERPROFILE%\.clickrun\clickrun.log`.

**VS Code:** Stable uses `Code`, Insiders uses `Code - Insiders`. Agent confirmation labels can include keyboard hints such as `Allow (Ctrl+Enter)`; the default config uses `prefixMatchLabels` for these.

**Antigravity:** Requires `ELECTRON_FORCE_RENDERER_ACCESSIBILITY=1` set as a user environment variable. Without it, the accessibility tree is empty and no buttons will be detected. Antigravity IDE uses `Antigravity IDE`; the Agent Manager uses `Antigravity`.

Do not add a new permission label to the whitelist without checking its exact accessible name and safety implications.

## Release Checklist

- Run the Debug build and complete test suite (`dotnet test tests/ClickRun.Tests/ClickRun.Tests.csproj`).
- Confirm the test count and pass/fail totals in the test output.
- Test VS Code Stable/Insiders and Antigravity IDE prompts in dry-run mode.
- Verify the project and installer versions match (`ClickRun.csproj` and `installer/clickrun-setup.iss`).
- Update `CHANGELOG.md` and relevant documentation for user-visible changes.
- Build the release binary: `dotnet publish src/ClickRun/ClickRun.csproj -c Release`
- Tag the release and create a GitHub release with the binary attached.

## Project Conventions

- C# with nullable reference types enabled
- Models use records where immutability is appropriate
- Static classes for stateless utilities (TitleMatcher, ButtonPrioritizer, ConfigParser)
- Instance classes for stateful components (SafetyFilter, Clicker, Detector, DebounceTracker, KillSwitch)
- Serilog for all logging — never `Console.WriteLine` in production code
- camelCase JSON property naming via `System.Text.Json`

## Adding a New Target Application

1. Add a new entry to the `whitelist` array in `~/.clickrun/config.json`
2. Set the correct `processName` (check Task Manager → Details tab)
3. Add `windowTitles` with appropriate match mode
4. Add the exact button labels you want to auto-click (order matters — earlier = higher priority)
5. Test with `"dryRun": true` first

No code changes needed — it's config-only.

## Adding a New Safety Check

1. Add the check to `SafetyFilter.Check()` before the whitelist loop
2. Return a `Reject()` with a descriptive reason string
3. Add the reason string to the `rejectionCounters` dictionary in `Program.RunScanLoop()`
4. Add the counter to the `LogCycleSummary()` format string
5. Add unit tests in `SafetyFilterTests.cs`

## Pull Request Guidelines

- Keep changes minimal and focused
- Don't break existing safety mechanisms
- Add tests for new logic
- Test with dry-run mode before submitting
- Update docs if behavior changes

## Architecture Principles

- Safety first: every change must maintain or improve safety
- Deterministic: same input always produces same output
- Minimal: no unnecessary features, dependencies, or complexity
- Observable: every decision is logged and traceable
