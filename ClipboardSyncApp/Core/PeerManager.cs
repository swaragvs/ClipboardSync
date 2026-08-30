using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class PeerManager
{
    private readonly List<ConnectionProfile> _profiles;

    public PeerManager()
    {
        _profiles = ConnectionStore.Load();
    }

    public IReadOnlyList<ConnectionProfile> Profiles => _profiles;

    public void AddOrUpdate(ConnectionProfile profile)
    {
        var existing = _profiles.FirstOrDefault(x => string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            _profiles.Add(profile);
        }
        else
        {
            var index = _profiles.IndexOf(existing);
            _profiles[index] = profile;
        }

        ConnectionStore.Save(_profiles);
    }

    public void Remove(string id)
    {
        var removed = _profiles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            ConnectionStore.Save(_profiles);
        }
    }

    public IReadOnlyList<ConnectionProfile> GetAutoConnectProfiles()
    {
        return _profiles.Where(x => x.AutoConnect).ToList();
    }
}
