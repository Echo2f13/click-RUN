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

The suite contains 337 tests across 10 test files, including VS Code compatibility, trust fallback integration, adversarial, and property-based coverage. The current verified baseline is 337 passed, 0 failed, and 0 skipped.

- `SafetyFilterTests.cs` — whitelist matching, blocklist, wildcard safety, rejection reasons
- `ButtonPrioritizerTests.cs` — intent priority and multi-candidate selection
- `DebounceTrackerTests.cs` — hash computation, cooldown, pruning, and collision resistance
- `TrustFallback*Tests.cs` — trust-dialog detection, safety checks, integration, adversarial, and property tests
- `VsCodeCompatibilityTests.cs` — VS Code Stable/Insiders labels, title matching, and keyboard fallback context gates
- `LoggerSetupTests.cs` — log level parsing and logger creation

## VS Code Agent Validation

ClickRun is a Windows tray application and should be validated on Windows with VS Code Stable or Insiders installed. Before enabling live clicks, use a configuration with `dryRun` set to `true`, debug logging enabled, and only the intended VS Code process and window-title entries whitelisted. Confirm a real agent prompt produces `Result=PASS` and `[DRY RUN] Would click` entries in `%USERPROFILE%\\.clickrun\\clickrun.log`.

VS Code Stable uses the `Code` process name and VS Code Insiders uses `Code - Insiders`. Agent confirmation labels can include keyboard hints such as `Allow (Ctrl+Enter)`; the default configuration uses `prefixMatchLabels` for these dynamic suffixes. Do not add a new permission label to the whitelist without checking its exact accessible name and safety implications.

## Release Checklist

- Run the Debug build and complete test suite.
- Confirm the test count and pass/fail totals in the test output.
- Test VS Code Stable and Insiders prompts in dry-run mode.
- Verify the project and installer versions match.
- Update `CHANGELOG.md` and relevant documentation for user-visible changes.
- Build the installer on Windows with the .NET 8 SDK and Inno Setup 6 installed.

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
