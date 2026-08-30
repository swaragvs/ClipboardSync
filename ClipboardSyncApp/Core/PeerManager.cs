using System.Collections.Concurrent;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class PeerManager : IDisposable
{
    private readonly List<ConnectionProfile> _profiles;
    private readonly ConcurrentDictionary<string, PeerConnection> _activeConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _instanceId;
    private readonly object _profileLock = new();

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<ClipboardPayload>? PayloadReceived;

    public PeerManager(string? instanceId = null)
    {
        _instanceId = instanceId ?? Guid.NewGuid().ToString("N");
        lock (_profileLock)
        {
            _profiles = ConnectionStore.Load();
        }
    }

    public IReadOnlyList<ConnectionProfile> Profiles
    {
        get
        {
            lock (_profileLock)
            {
                return _profiles.ToList();
            }
        }
    }

    public void InitializeConnections()
    {
        lock (_profileLock)
        {
            foreach (var profile in _profiles)
            {
                GetOrCreateConnection(profile);
            }
        }
    }

    public PeerConnection GetOrCreateConnection(ConnectionProfile profile)
    {
        return _activeConnections.GetOrAdd(profile.Id, id =>
        {
            var conn = new PeerConnection(profile, _instanceId);
            conn.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
            conn.PayloadReceived += (_, payload) => PayloadReceived?.Invoke(this, payload);
            conn.Start();
            return conn;
        });
    }

    public void AddOrUpdate(ConnectionProfile profile)
    {
        lock (_profileLock)
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

        if (_activeConnections.TryRemove(profile.Id, out var oldConn))
        {
            oldConn.Dispose();
        }

        GetOrCreateConnection(profile);
    }

    public void Remove(string id)
    {
        lock (_profileLock)
        {
            var removed = _profiles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                ConnectionStore.Save(_profiles);
            }
        }

        if (_activeConnections.TryRemove(id, out var conn))
        {
            conn.Dispose();
        }
    }

    public IReadOnlyList<ConnectionProfile> GetAutoConnectProfiles()
    {
        lock (_profileLock)
        {
            return _profiles.Where(x => x.AutoConnect).ToList();
        }
    }

    public void FanOutPayload(ClipboardPayload payload)
    {
        foreach (var conn in _activeConnections.Values)
        {
            if (conn.Profile.AutoConnect || conn.IsOnline)
            {
                conn.EnqueuePayload(payload);
            }
        }
    }

    public void Dispose()
    {
        foreach (var conn in _activeConnections.Values)
        {
            conn.Dispose();
        }
        _activeConnections.Clear();
    }
}
