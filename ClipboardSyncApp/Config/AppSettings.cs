using System.Text.Json;

namespace ClipboardSyncApp.Config;

public sealed class AppSettings
{
    private const string SettingsFileName = "settings.json";

    public int Port { get; set; } = 5001;
    public int MaxQueueDepth { get; set; } = 20;
    public int MaxImageSizeMB { get; set; } = 25;
    public int MaxIncomingFileSizeMB { get; set; } = 2048;
    public int MaxConcurrentTransfers { get; set; } = 2;
    public string ReceivedFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync", "Received");

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool PauseHistory { get; set; }
    public int HistoryMaxItems { get; set; } = 200;
    public int HistoryMaxDaysOld { get; set; } = 30;

    public void Validate()
    {
        if (Port < 1 || Port > 65535)
        {
            Port = 5001;
        }
        if (MaxQueueDepth < 1 || MaxQueueDepth > 100)
        {
            MaxQueueDepth = 20;
        }
        if (MaxImageSizeMB < 1 || MaxImageSizeMB > 50)
        {
            MaxImageSizeMB = 25;
        }
        if (MaxIncomingFileSizeMB < 1 || MaxIncomingFileSizeMB > 10240)
        {
            MaxIncomingFileSizeMB = 2048;
        }
        if (MaxConcurrentTransfers < 1 || MaxConcurrentTransfers > 10)
        {
            MaxConcurrentTransfers = 2;
        }
        if (HistoryMaxItems < 10 || HistoryMaxItems > 5000)
        {
            HistoryMaxItems = 200;
        }
        if (HistoryMaxDaysOld < 1 || HistoryMaxDaysOld > 365)
        {
            HistoryMaxDaysOld = 30;
        }
        if (string.IsNullOrWhiteSpace(ReceivedFolder))
        {
            ReceivedFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync", "Received");
        }
        try
        {
            Directory.CreateDirectory(ReceivedFolder);
        }
        catch
        {
            ReceivedFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync", "Received");
            Directory.CreateDirectory(ReceivedFolder);
        }
    }

    public static AppSettings Load()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);

        var settingsPath = Path.Combine(appDataPath, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new AppSettings();
            defaultSettings.Validate();
            defaultSettings.Save();
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            var loaded = settings ?? new AppSettings();
            loaded.Validate();
            return loaded;
        }
        catch
        {
            var fallback = new AppSettings();
            fallback.Validate();
            fallback.Save();
            return fallback;
        }
    }

    public void Save()
    {
        Validate();
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);

        var settingsPath = Path.Combine(appDataPath, SettingsFileName);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }
}
