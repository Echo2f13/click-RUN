using System.Diagnostics;
using System.IO.Compression;
using Serilog;

namespace ClickRun.Updates;

/// <summary>
/// Handles installing updates by creating a batch script that runs after the app exits.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>
    /// Prepares and launches the update installer.
    /// The installer waits for the current process to exit, then replaces the exe and restarts.
    /// </summary>
    public static bool InstallUpdate(string downloadedFile, ILogger logger)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                logger.Error("Cannot determine current executable path");
                return false;
            }

            var currentDir = Path.GetDirectoryName(currentExe)!;
            var currentPid = Environment.ProcessId;

            // Determine the source file (extract if zip)
            string sourceExe;
            string? tempExtractDir = null;

            if (downloadedFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tempExtractDir = Path.Combine(Path.GetTempPath(), $"ClickRun_Extract_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempExtractDir);
                ZipFile.ExtractToDirectory(downloadedFile, tempExtractDir);

                // Find the exe in the extracted folder
                sourceExe = Directory.GetFiles(tempExtractDir, "ClickRun.exe", SearchOption.AllDirectories)
                    .FirstOrDefault() ?? "";

                if (string.IsNullOrEmpty(sourceExe))
                {
                    logger.Error("ClickRun.exe not found in update package");
                    return false;
                }
            }
            else
            {
                sourceExe = downloadedFile;
            }

            // Create the update batch script
            var batchPath = Path.Combine(Path.GetTempPath(), $"ClickRun_Update_{Guid.NewGuid():N}.bat");
            var batchContent = $"""
                @echo off
                title ClickRun Updater
                echo Waiting for ClickRun to close...
                
                :waitloop
                tasklist /FI "PID eq {currentPid}" 2>NUL | find /I "{currentPid}" >NUL
                if "%ERRORLEVEL%"=="0" (
                    timeout /t 1 /nobreak >NUL
                    goto waitloop
                )
                
                echo Updating ClickRun...
                timeout /t 1 /nobreak >NUL
                
                copy /Y "{sourceExe}" "{currentExe}"
                if errorlevel 1 (
                    echo Update failed! Press any key to exit.
                    pause >NUL
                    exit /b 1
                )
                
                echo Update complete! Restarting ClickRun...
                timeout /t 1 /nobreak >NUL
                
                start "" "{currentExe}"
                
                REM Cleanup
                del /Q "{downloadedFile}" 2>NUL
                {(tempExtractDir != null ? $"rmdir /S /Q \"{tempExtractDir}\" 2>NUL" : "")}
                del "%~f0"
                """;

            File.WriteAllText(batchPath, batchContent);

            // Launch the batch script hidden
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C \"{batchPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
            logger.Information("Update installer launched, application will restart after update");

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to prepare update installer");
            return false;
        }
    }
}
