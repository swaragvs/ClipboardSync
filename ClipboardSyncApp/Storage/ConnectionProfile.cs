namespace ClipboardSyncApp.Storage;

public sealed class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Unnamed peer";
    public string TailscaleIp { get; set; } = string.Empty;
    public int Port { get; set; } = 5001;
    public DateTime LastConnectedUtc { get; set; } = DateTime.MinValue;
    public bool AutoConnect { get; set; }
    public string? SharedKey { get; set; }
}
