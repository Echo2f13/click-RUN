using ClickRun.Config;
using ClickRun.Logging;
using ClickRun.Tray;
using Serilog;

namespace ClickRun;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Single instance guard
        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            // Another instance is already running — exit silently
            return;
        }

        ILogger? logger = null;

        try
        {
            var config = DefaultConfig.LoadOrCreateDefault();
            logger = LoggerSetup.CreateLogger(config.LogLevel);

            logger.Information("ClickRun starting on platform: Windows (tray mode)");
            logger.Information(
                "Config loaded — ScanInterval={ScanIntervalMs}ms, DebounceCooldown={DebounceCooldownMs}ms, " +
                "KillSwitch={KillSwitchHotkey}, WildcardEnabled={EnableWildcard}, WhitelistEntries={WhitelistCount}, " +
                "DebugInstrumentation={DebugInstrumentation}, DryRun={DryRun}, PreClickDelayMs={PreClickDelayMs}, " +
                "MultiWindowMode={MultiWindowMode}",
                config.ScanIntervalMs,
                config.DebounceCooldownMs,
                config.KillSwitchHotkey,
                config.EnableWildcardProcess,
                config.Whitelist.Count,
                config.EnableDebugInstrumentation,
                config.DryRun,
                config.PreClickDelayMs,
                config.MultiWindowMode);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp(config, logger));
        }
        catch (InvalidOperationException ex)
        {
            logger?.Error(ex, "Configuration error");
            MessageBox.Show($"Click Run configuration error:\n\n{ex.Message}",
                "Click Run", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            logger?.Error(ex, "Fatal error in ClickRun");
            MessageBox.Show($"Click Run fatal error:\n\n{ex.Message}",
                "Click Run", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
