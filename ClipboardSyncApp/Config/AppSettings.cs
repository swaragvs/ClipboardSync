using System.Text.Json;

namespace ClipboardSyncApp.Config;

public sealed class AppSettings
{
    private const string SettingsFileName = "settings.json";

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool PauseHistory { get; set; }
    public int HistoryMaxItems { get; set; } = 200;
    public int HistoryMaxDaysOld { get; set; } = 30;

    public static AppSettings Load()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);

        var settingsPath = Path.Combine(appDataPath, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new AppSettings();
            defaultSettings.Save();
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            var fallback = new AppSettings();
            fallback.Save();
            return fallback;
        }
    }

    public void Save()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync");
        Directory.CreateDirectory(appDataPath);

        var settingsPath = Path.Combine(appDataPath, SettingsFileName);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }

    public void ApplyStartupSetting()
    {
        if (!StartWithWindows)
        {
            return;
        }

        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolder, "ClipboardSync.url");

        var appPath = Environment.ProcessPath ?? Application.ExecutablePath;
        var content = "[InternetShortcut]\r\nURL=file:///" + appPath + "\r\nIconIndex=0\r\n";
        File.WriteAllText(shortcutPath, content);
    }
}
