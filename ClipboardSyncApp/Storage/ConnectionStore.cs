using System.Text.Json;

namespace ClipboardSyncApp.Storage;

public sealed class ConnectionStore
{
    private const string FileName = "peers.json";

    public static string GetStorePath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, FileName);
    }

    public static List<ConnectionProfile> Load()
    {
        var path = GetStorePath();
        if (!File.Exists(path))
        {
            return new List<ConnectionProfile>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ConnectionProfile>>(json);
            return list ?? new List<ConnectionProfile>();
        }
        catch
        {
            return new List<ConnectionProfile>();
        }
    }

    public static void Save(List<ConnectionProfile> profiles)
    {
        var path = GetStorePath();
        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static void Upsert(ConnectionProfile profile)
    {
        var profiles = Load();
        var existingIndex = profiles.FindIndex(x => string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        Save(profiles);
    }

    public static void Delete(string id)
    {
        var profiles = Load();
        var filtered = profiles.Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        Save(filtered);
    }
}
