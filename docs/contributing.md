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

### Run Tests
```bash
dotnet test tests/ClickRun.Tests/ClickRun.Tests.csproj
```

### Run
```bash
dotnet run --project src/ClickRun/ClickRun.csproj
```

## Test Suite

60 tests across 4 test files:
- `SafetyFilterTests.cs` — whitelist matching, blocklist, wildcard safety, rejection reasons
- `ButtonPrioritizerTests.cs` — keyword priority, exact vs substring, tie-breaking, multi-candidate selection
- `DebounceTrackerTests.cs` — hash computation, cooldown, pruning, null AutomationId handling
- `LoggerSetupTests.cs` — log level parsing, logger creation

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
