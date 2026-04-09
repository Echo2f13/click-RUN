using System.Text.Json;
using ClickRun.Models;

namespace ClickRun.Config;

/// <summary>
/// Serializes a Configuration object to JSON with 2-space indentation.
/// </summary>
public static class ConfigSerializer
{
    /// <summary>
    /// Serializes the configuration to a JSON string with 2-space indentation and camelCase naming.
    /// </summary>
    public static string Serialize(Configuration config)
    {
        var options = ConfigParser.GetSerializerOptions();
        return JsonSerializer.Serialize(config, options);
    }

    /// <summary>
    /// Writes the configuration to the specified file path, creating directories as needed.
    /// </summary>
    public static void SaveToFile(Configuration config, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = Serialize(config);
        File.WriteAllText(filePath, json);
    }
}
