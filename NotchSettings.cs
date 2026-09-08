using System.Text.Json;

namespace NotchSense;

public enum NotchEdge { Left, Right }

public sealed class NotchSettings
{
    public int LayoutVersion { get; set; } = 3;
    public NotchEdge Edge { get; set; } = NotchEdge.Right;
    public bool StartWithWindows { get; set; }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchSense");
    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");
    private static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareNotch", "settings.json");

    public static NotchSettings Load()
    {
        try
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (!File.Exists(path)) return new NotchSettings();

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var settings = new NotchSettings
            {
                LayoutVersion = root.TryGetProperty("LayoutVersion", out var version) ? version.GetInt32() : 1,
                StartWithWindows = root.TryGetProperty("StartWithWindows", out var startup) && startup.GetBoolean(),
                Edge = ReadEdge(root, out var wasLegacyEdge) 
            };

            if (settings.LayoutVersion < 3 || wasLegacyEdge || !string.Equals(path, SettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                settings.LayoutVersion = 3;
                settings.Save();
            }

            return settings;
        }
        catch
        {
            return new NotchSettings();
        }
    }

    private static NotchEdge ReadEdge(JsonElement root, out bool wasLegacyEdge)
    {
        wasLegacyEdge = false;
        if (!root.TryGetProperty("Edge", out var edge)) return NotchEdge.Right;

        // Versions 1/2 stored the enum numerically as:
        // Top=0, Bottom=1, Left=2, Right=3.
        if (edge.ValueKind == JsonValueKind.Number && edge.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                2 => NotchEdge.Left,
                3 => NotchEdge.Right,
                _ => MarkLegacyAndReturnRight(ref wasLegacyEdge)
            };
        }

        if (edge.ValueKind == JsonValueKind.String)
        {
            return edge.GetString()?.ToLowerInvariant() switch
            {
                "left" => NotchEdge.Left,
                "right" => NotchEdge.Right,
                _ => MarkLegacyAndReturnRight(ref wasLegacyEdge)
            };
        }

        return NotchEdge.Right;
    }

    private static NotchEdge MarkLegacyAndReturnRight(ref bool wasLegacyEdge)
    {
        wasLegacyEdge = true;
        return NotchEdge.Right;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
