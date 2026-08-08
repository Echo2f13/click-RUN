using System.Diagnostics;
using ClickRun.Engine;
using ClickRun.Models;
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

    private static readonly string AppVersion = typeof(TrayApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly NotifyIcon _trayIcon;
    private readonly ClickRunEngine _engine;
    private readonly ILogger _logger;
    private readonly Configuration _config;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly string _logFilePath;
    private bool _wasPausedBeforeMenuOpen;

    public TrayApp(Configuration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".clickrun", "clickrun.log");

        _engine = new ClickRunEngine(config, logger);

        // Build context menu
        _statusItem = new ToolStripMenuItem($"Click Run v{AppVersion} — Running") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Pause", null, OnPauseResume);
        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutoStart)
        {
            Checked = IsAutoStartEnabled()
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Open Logs", null, OnOpenLogs));
        menu.Items.Add(new ToolStripMenuItem("Open Config", null, OnOpenConfig));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        // Pause scanning when menu is open to ensure user can interact with it
        menu.Opening += OnMenuOpening;
        menu.Closed += OnMenuClosed;

        // Format tooltip with kill switch hotkey for visibility
        var tooltipText = FormatTooltip("Running");

        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = tooltipText,
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => OnPauseResume(this, EventArgs.Empty);

        // Start the engine
        _engine.Start();
    }

    /// <summary>
    /// Formats the tray tooltip to include version, status, and kill switch hotkey.
    /// Note: Windows limits tooltip text to 63 characters, so we keep it concise.
    /// </summary>
    private string FormatTooltip(string status)
    {
        // "Click Run v1.3.0 — Running (Ctrl+Alt+R)" = ~42 chars, safe under 63 limit
        return $"Click Run v{AppVersion} — {status} ({_config.KillSwitchHotkey})";
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save current pause state and pause the engine while menu is open
        // This ensures the user can interact with the menu without focus stealing
        _wasPausedBeforeMenuOpen = _engine.IsPaused;
        if (!_wasPausedBeforeMenuOpen)
        {
            _engine.Pause();
            _logger.Debug("Engine paused while tray menu is open");
        }
    }

    private void OnMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        // Restore previous pause state when menu closes
        // Only resume if the engine wasn't already paused before opening
        if (!_wasPausedBeforeMenuOpen && _engine.IsPaused)
        {
            _engine.Resume();
            _logger.Debug("Engine resumed after tray menu closed");
        }
    }

    private static Icon LoadIcon()
    {
        // Try to load custom icon from the app directory
        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        // Fallback: use the default application icon
        return SystemIcons.Application;
    }

    private void OnPauseResume(object? sender, EventArgs e)
    {
        _engine.TogglePause();

        if (_engine.IsPaused)
        {
            _statusItem.Text = $"Click Run v{AppVersion} — Paused";
            _pauseItem.Text = "Resume";
            _trayIcon.Text = FormatTooltip("Paused");
        }
        else
        {
            _statusItem.Text = $"Click Run v{AppVersion} — Running";
            _pauseItem.Text = "Pause";
            _trayIcon.Text = FormatTooltip("Running");
        }
    }

    private void OnOpenLogs(object? sender, EventArgs e)
    {
        try
        {
            // Open the log directory — user can pick the latest file
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
            var configPath = Config.DefaultConfig.GetDefaultConfigPath();
            if (File.Exists(configPath))
                Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open config file");
        }
    }

    private void OnToggleAutoStart(object? sender, EventArgs e)
    {
        if (IsAutoStartEnabled())
        {
            RemoveAutoStart();
            _autoStartItem.Checked = false;
        }
        else
        {
            SetAutoStart();
            _autoStartItem.Checked = true;
        }
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

    private void OnExit(object? sender, EventArgs e)
    {
        _engine.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _logger.Information("ClickRun exiting via tray menu.");
        Log.CloseAndFlush();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
