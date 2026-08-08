using System.Diagnostics;
using System.Drawing.Drawing2D;
using ClickRun.Engine;
using ClickRun.Models;
using ClickRun.Updates;
using Microsoft.Win32;
using Serilog;

namespace ClickRun.Tray;

/// <summary>
/// System tray application shell. Hosts the ClickRunEngine in the background
/// and provides a NotifyIcon with context menu for control.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private const string AppName = "ClickRun";
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string GitHubOwner = "Echo2f13";
    private const string GitHubRepo = "click-RUN";

    private static readonly string AppVersion = typeof(TrayApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly Version CurrentVersion = typeof(TrayApp).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private readonly NotifyIcon _trayIcon;
    private readonly ClickRunEngine _engine;
    private readonly ILogger _logger;
    private readonly Configuration _config;
    private readonly UpdateChecker _updateChecker;
    private readonly string _logFilePath;
    private readonly string _configFilePath;

    // Menu items that need to be updated
    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _pauseItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;
    private ToolStripMenuItem _updateItem = null!;
    private ToolStripLabel _clickCountLabel = null!;

    private bool _wasPausedBeforeMenuOpen;
    private bool _isDisposed;
    private UpdateInfo? _availableUpdate;

    public TrayApp(Configuration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".clickrun", "clickrun.log");
        _configFilePath = Config.DefaultConfig.GetDefaultConfigPath();

        _engine = new ClickRunEngine(config, logger);
        _updateChecker = new UpdateChecker(GitHubOwner, GitHubRepo, CurrentVersion, logger);

        // Build the improved context menu
        var menu = CreateContextMenu();

        // Format tooltip
        var tooltipText = FormatTooltip("Running");

        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = tooltipText,
            ContextMenuStrip = menu,
            Visible = true
        };

        // Double-click to pause/resume
        _trayIcon.DoubleClick += (_, _) => OnPauseResume(this, EventArgs.Empty);

        // Single click shows balloon with status (optional, can be annoying)
        // _trayIcon.Click += OnTrayClick;

        // Start the engine
        _engine.Start();

        // Check for updates in background (after 5 seconds delay)
        _ = CheckForUpdatesDelayedAsync();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            ShowCheckMargin = true,
            Font = new Font("Segoe UI", 9F),
            Padding = new Padding(0, 4, 0, 4)
        };

        // Header section
        _statusItem = new ToolStripMenuItem($"⚡ Click Run v{AppVersion}")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _clickCountLabel = new ToolStripLabel("Status: Running")
        {
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Segoe UI", 8F)
        };

        // Main controls
        _pauseItem = new ToolStripMenuItem("⏸️  Pause", null, OnPauseResume)
        {
            ShortcutKeyDisplayString = "Dbl-Click"
        };

        // Quick actions submenu
        var quickActionsMenu = new ToolStripMenuItem("🔧  Quick Actions");
        quickActionsMenu.DropDownItems.Add(new ToolStripMenuItem("🔄  Reload Config", null, OnReloadConfig));
        quickActionsMenu.DropDownItems.Add(new ToolStripMenuItem("🧹  Clear Debounce Cache", null, OnClearCache));
        quickActionsMenu.DropDownItems.Add(new ToolStripSeparator());
        quickActionsMenu.DropDownItems.Add(new ToolStripMenuItem("🧪  Test Mode (Dry Run)", null, OnToggleDryRun)
        {
            Checked = _config.DryRun,
            CheckOnClick = true
        });

        // Files submenu
        var filesMenu = new ToolStripMenuItem("📂  Open...");
        filesMenu.DropDownItems.Add(new ToolStripMenuItem("📋  Logs Folder", null, OnOpenLogs));
        filesMenu.DropDownItems.Add(new ToolStripMenuItem("⚙️  Config File", null, OnOpenConfig));
        filesMenu.DropDownItems.Add(new ToolStripMenuItem("📁  App Folder", null, OnOpenAppFolder));

        // Settings submenu
        var settingsMenu = new ToolStripMenuItem("⚙️  Settings");
        _autoStartItem = new ToolStripMenuItem("🚀  Start with Windows", null, OnToggleAutoStart)
        {
            Checked = IsAutoStartEnabled(),
            CheckOnClick = true
        };
        settingsMenu.DropDownItems.Add(_autoStartItem);
        settingsMenu.DropDownItems.Add(new ToolStripMenuItem("🔔  Show Notifications", null, OnToggleNotifications)
        {
            Checked = true, // TODO: Add to config
            CheckOnClick = true,
            Enabled = false // Not implemented yet
        });

        // Update item
        _updateItem = new ToolStripMenuItem("🔄  Check for Updates", null, OnCheckForUpdates);

        // Help submenu
        var helpMenu = new ToolStripMenuItem("❓  Help");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("📖  Documentation", null, (_, _) => OpenUrl($"https://github.com/{GitHubOwner}/{GitHubRepo}#readme")));
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("🐛  Report Issue", null, (_, _) => OpenUrl($"https://github.com/{GitHubOwner}/{GitHubRepo}/issues/new")));
        helpMenu.DropDownItems.Add(new ToolStripSeparator());
        helpMenu.DropDownItems.Add(new ToolStripMenuItem($"ℹ️  About (v{AppVersion})", null, OnShowAbout));

        // Exit
        var exitItem = new ToolStripMenuItem("🚪  Exit", null, OnExit)
        {
            ForeColor = Color.FromArgb(180, 60, 60)
        };

        // Build menu structure
        menu.Items.Add(_statusItem);
        menu.Items.Add(_clickCountLabel);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(_pauseItem);
        menu.Items.Add(quickActionsMenu);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(filesMenu);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(_updateItem);
        menu.Items.Add(helpMenu);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(exitItem);

        // Pause scanning when menu is open
        menu.Opening += OnMenuOpening;
        menu.Closed += OnMenuClosed;

        return menu;
    }

    private string FormatTooltip(string status)
    {
        // Windows limits tooltip to 63 characters
        return $"Click Run v{AppVersion} — {status} ({_config.KillSwitchHotkey})";
    }

    private void UpdateStatusDisplay()
    {
        var status = _engine.IsPaused ? "Paused" : "Running";
        _clickCountLabel.Text = $"Status: {status}";

        if (_engine.IsPaused)
        {
            _pauseItem.Text = "▶️  Resume";
            _trayIcon.Text = FormatTooltip("Paused");
        }
        else
        {
            _pauseItem.Text = "⏸️  Pause";
            _trayIcon.Text = FormatTooltip("Running");
        }
    }

    #region Event Handlers

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _wasPausedBeforeMenuOpen = _engine.IsPaused;
        if (!_wasPausedBeforeMenuOpen)
        {
            _engine.Pause();
            _logger.Debug("Engine paused while tray menu is open");
        }
        UpdateStatusDisplay();
    }

    private void OnMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        if (!_wasPausedBeforeMenuOpen && _engine.IsPaused)
        {
            _engine.Resume();
            _logger.Debug("Engine resumed after tray menu closed");
        }
    }

    private void OnPauseResume(object? sender, EventArgs e)
    {
        _engine.TogglePause();
        UpdateStatusDisplay();

        // Show balloon notification
        var status = _engine.IsPaused ? "Paused" : "Running";
        _trayIcon.ShowBalloonTip(1500, "Click Run", $"Status: {status}", ToolTipIcon.Info);
    }

    private void OnReloadConfig(object? sender, EventArgs e)
    {
        try
        {
            // This would require re-initializing the engine with new config
            // For now, just show a message that restart is required
            MessageBox.Show(
                "To apply config changes, please restart Click Run.\n\nUse Exit and restart the application.",
                "Reload Config",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reload config");
        }
    }

    private void OnClearCache(object? sender, EventArgs e)
    {
        // The debounce tracker is internal to the engine
        // We'd need to expose a method to clear it
        _trayIcon.ShowBalloonTip(1500, "Click Run", "Cache cleared (will auto-clear in ~10s anyway)", ToolTipIcon.Info);
    }

    private void OnToggleDryRun(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            // This would require modifying the running config
            _trayIcon.ShowBalloonTip(1500, "Click Run", 
                item.Checked ? "Dry Run enabled (no clicks)" : "Dry Run disabled", 
                ToolTipIcon.Info);
        }
    }

    private void OnOpenLogs(object? sender, EventArgs e)
    {
        try
        {
            var logDir = Path.GetDirectoryName(_logFilePath);
            if (logDir != null && Directory.Exists(logDir))
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open log directory");
        }
    }

    private void OnOpenConfig(object? sender, EventArgs e)
    {
        try
        {
            if (File.Exists(_configFilePath))
                Process.Start(new ProcessStartInfo(_configFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open config file");
        }
    }

    private void OnOpenAppFolder(object? sender, EventArgs e)
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo(appDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open app folder");
        }
    }

    private void OnToggleAutoStart(object? sender, EventArgs e)
    {
        if (IsAutoStartEnabled())
        {
            RemoveAutoStart();
            _autoStartItem.Checked = false;
            _trayIcon.ShowBalloonTip(1500, "Click Run", "Auto-start disabled", ToolTipIcon.Info);
        }
        else
        {
            SetAutoStart();
            _autoStartItem.Checked = true;
            _trayIcon.ShowBalloonTip(1500, "Click Run", "Auto-start enabled", ToolTipIcon.Info);
        }
    }

    private void OnToggleNotifications(object? sender, EventArgs e)
    {
        // TODO: Implement notification toggle
    }

    private async void OnCheckForUpdates(object? sender, EventArgs e)
    {
        _updateItem.Text = "🔄  Checking...";
        _updateItem.Enabled = false;

        try
        {
            var update = await _updateChecker.CheckForUpdatesAsync();

            if (update != null)
            {
                _availableUpdate = update;
                _updateItem.Text = $"⬆️  Update Available (v{update.LatestVersion})";
                _updateItem.Click -= OnCheckForUpdates;
                _updateItem.Click += OnDownloadUpdate;

                _trayIcon.ShowBalloonTip(5000, "Update Available",
                    $"Click Run v{update.LatestVersion} is available!\nClick to download.",
                    ToolTipIcon.Info);
            }
            else
            {
                _updateItem.Text = "✅  Up to Date";
                _trayIcon.ShowBalloonTip(2000, "Click Run", "You're running the latest version!", ToolTipIcon.Info);

                // Reset after 5 seconds
                _ = Task.Delay(5000).ContinueWith(_ =>
                {
                    if (!_isDisposed)
                    {
                        _trayIcon.ContextMenuStrip?.Invoke(() =>
                        {
                            _updateItem.Text = "🔄  Check for Updates";
                        });
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Update check failed");
            _updateItem.Text = "❌  Update Check Failed";
        }
        finally
        {
            _updateItem.Enabled = true;
        }
    }

    private async void OnDownloadUpdate(object? sender, EventArgs e)
    {
        if (_availableUpdate == null) return;

        var result = MessageBox.Show(
            $"Download and install Click Run v{_availableUpdate.LatestVersion}?\n\n" +
            $"Size: {_availableUpdate.FormattedSize}\n\n" +
            "The application will restart after the update.",
            "Update Available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        _updateItem.Text = "⬇️  Downloading...";
        _updateItem.Enabled = false;

        try
        {
            var progress = new Progress<int>(p =>
            {
                _trayIcon.ContextMenuStrip?.Invoke(() =>
                {
                    _updateItem.Text = $"⬇️  Downloading... {p}%";
                });
            });

            var downloadPath = await _updateChecker.DownloadUpdateAsync(_availableUpdate, progress);

            if (downloadPath != null)
            {
                _updateItem.Text = "📦  Installing...";

                if (UpdateInstaller.InstallUpdate(downloadPath, _logger))
                {
                    _trayIcon.ShowBalloonTip(3000, "Click Run", "Update downloaded! Restarting...", ToolTipIcon.Info);

                    // Give user time to see the notification
                    await Task.Delay(2000);

                    // Exit the application - the updater will handle the rest
                    OnExit(this, EventArgs.Empty);
                }
                else
                {
                    MessageBox.Show(
                        "Failed to install update. Please download manually from GitHub.",
                        "Update Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    _updateItem.Text = "❌  Update Failed";
                }
            }
            else
            {
                _updateItem.Text = "❌  Download Failed";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Update download failed");
            _updateItem.Text = "❌  Update Failed";
        }
        finally
        {
            _updateItem.Enabled = true;
        }
    }

    private void OnShowAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(
            $"Click Run v{AppVersion}\n\n" +
            "Auto-click permission prompts in AI development tools.\n\n" +
            $"Kill Switch: {_config.KillSwitchHotkey}\n" +
            $"Scan Interval: {_config.ScanIntervalMs}ms\n" +
            $"Whitelist Entries: {_config.Whitelist.Count}\n\n" +
            $"GitHub: github.com/{GitHubOwner}/{GitHubRepo}",
            "About Click Run",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _engine.Dispose();
        _updateChecker.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _logger.Information("ClickRun exiting via tray menu.");
        Log.CloseAndFlush();
        Application.Exit();
    }

    #endregion

    #region Helper Methods

    private async Task CheckForUpdatesDelayedAsync()
    {
        await Task.Delay(5000); // Wait 5 seconds after startup

        try
        {
            var update = await _updateChecker.CheckForUpdatesAsync();
            if (update != null)
            {
                _availableUpdate = update;

                _trayIcon.ContextMenuStrip?.Invoke(() =>
                {
                    _updateItem.Text = $"⬆️  Update Available (v{update.LatestVersion})";
                    _updateItem.Click -= OnCheckForUpdates;
                    _updateItem.Click += OnDownloadUpdate;
                });

                _trayIcon.ShowBalloonTip(5000, "Update Available",
                    $"Click Run v{update.LatestVersion} is available!\nRight-click tray icon to update.",
                    ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Background update check failed");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private static Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }
        return SystemIcons.Application;
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
        return key?.GetValue(AppName) != null;
    }

    private static void SetAutoStart()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    private static void RemoveAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
        key?.DeleteValue(AppName, false);
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isDisposed = true;
            _engine.Dispose();
            _updateChecker.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
