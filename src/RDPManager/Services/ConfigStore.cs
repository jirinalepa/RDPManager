using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RDPManager.Models;

namespace RDPManager.Services;

/// <summary>Persists settings and connections as JSON in %APPDATA%\RDPManager.</summary>
public class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RDPManager");

    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private static string ConnectionsPath => Path.Combine(DataDirectory, "connections.json");

    public AppSettings LoadSettings() => Load<AppSettings>(SettingsPath) ?? new AppSettings();

    public List<Connection> LoadConnections() => Load<List<Connection>>(ConnectionsPath) ?? [];

    public void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    public void SaveConnections(IEnumerable<Connection> connections) => Save(ConnectionsPath, connections.ToList());

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            // Keep the broken file for manual recovery and start with defaults.
            File.Copy(path, path + ".broken", overwrite: true);
            return default;
        }
    }

    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(DataDirectory);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
