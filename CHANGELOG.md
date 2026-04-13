# Changelog

## v1.1.0

### System Tray App
- Converted from CLI to Windows tray application (WinExe, no console window)
- NotifyIcon with context menu: Running/Paused status, Pause/Resume, Open Logs, Open Config, Start with Windows, Exit
- Double-click tray icon to toggle pause/resume
- Auto-start on Windows login via registry (HKCU\...\Run)
- Single-instance guard via named Mutex

### Multi-Window Mode
- Scan all visible windows belonging to whitelisted processes (not just foreground)
- Catches permission prompts in background Kiro/VS Code windows
- Enable via `"multiWindowMode": true`

### Context-Aware "Yes" Clicking
- "Yes" buttons require context validation before clicking
- Extracts text from the UI tree around the button (dialog container, siblings)
- Safe keywords: "Allow write", "Permission", "Grant", "Make this edit", etc.
- Dangerous keywords: "Delete", "Remove", "Overwrite", etc. — hard reject
- Configurable via `contextRequiredLabels`, `safeContextKeywords`, `dangerousContextKeywords`

### Keyboard Fallback
- For Electron/webview panels where UI Automation can't click buttons
- Detects numbered options ("1 Yes", "2 No") in context text
- Focuses target window via SetForegroundWindow + AttachThreadInput
- Sends key via SendInput with proper timing delays
- Enable via `"enableKeyboardFallback": true`

### Keyword Priority
- Whitelist label order defines strict priority: Run > Allow > Accept > Trust
- "Trust command and accept" resolves to Accept (index 5) not Trust (index 8)

### Performance Fix
- Fixed ContextExtractor walking entire Electron window tree (289K chars, 7s per cycle)
- Now capped at 500 chars, max depth 3, only small containers (Group/Custom)
- Scan cycles back to ~1 second

### Installer
- Inno Setup installer (ClickRunSetup.exe)
- Installs to Program Files, Start Menu shortcut, optional desktop shortcut
- Optional auto-start on Windows startup
- Clean uninstall

## v1.0.0

Initial release. CLI-based auto-clicker for AI tool permission prompts using Windows UI Automation API.
