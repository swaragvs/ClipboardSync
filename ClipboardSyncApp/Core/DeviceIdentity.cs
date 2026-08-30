using System.Text.Json;

namespace ClipboardSyncApp.Core;

public sealed class DeviceIdentity
{
    private const string IdentityFileName = "device_identity.json";
    private static readonly object FileLock = new();
    private static string? _cachedPeerId;

    public string PeerId { get; set; } = Guid.NewGuid().ToString("N");

    public static string GetOrCreatePeerId()
    {
        lock (FileLock)
        {
            if (_cachedPeerId != null)
            {
                return _cachedPeerId;
            }

            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
            Directory.CreateDirectory(appDataPath);
            var path = Path.Combine(appDataPath, IdentityFileName);

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var identity = JsonSerializer.Deserialize<DeviceIdentity>(json);
                    if (identity != null && !string.IsNullOrWhiteSpace(identity.PeerId))
                    {
                        _cachedPeerId = identity.PeerId;
                        return _cachedPeerId;
                    }
                }
                catch
                {
                }
            }

            var newIdentity = new DeviceIdentity { PeerId = Guid.NewGuid().ToString("N") };
            var newJson = JsonSerializer.Serialize(newIdentity, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, newJson);
            _cachedPeerId = newIdentity.PeerId;
            return _cachedPeerId;
        }
    }
}
