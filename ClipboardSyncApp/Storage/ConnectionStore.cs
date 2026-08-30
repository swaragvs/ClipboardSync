using System.Text.Json;

namespace ClipboardSyncApp.Storage;

public sealed class ConnectionStore
{
    private const string FileName = "peers.json";
    private static readonly object FileLock = new();

    public static string GetStorePath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, FileName);
    }

    public static List<ConnectionProfile> Load()
    {
        lock (FileLock)
        {
            return LoadInternal();
        }
    }

    public static void Save(List<ConnectionProfile> profiles)
    {
        lock (FileLock)
        {
            SaveInternal(profiles);
        }
    }

    public static void Upsert(ConnectionProfile profile)
    {
        lock (FileLock)
        {
            var profiles = LoadInternal();
            var existingIndex = profiles.FindIndex(x => string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                profiles[existingIndex] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            SaveInternal(profiles);
        }
    }

    public static void Delete(string id)
    {
        lock (FileLock)
        {
            var profiles = LoadInternal();
            var filtered = profiles.Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            SaveInternal(filtered);
        }
    }

    private static List<ConnectionProfile> LoadInternal()
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
            // Corrupt file fallback
            return new List<ConnectionProfile>();
        }
    }

    private static void SaveInternal(List<ConnectionProfile> profiles)
    {
        var path = GetStorePath();
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(tempPath, json);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(tempPath, path);
    }
}
