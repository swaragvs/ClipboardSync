using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class AutoconnectManager : IDisposable
{
    private readonly PeerManager _peerManager;
    private readonly Func<ConnectionProfile, Task> _onConnectAttempt;
    private readonly Dictionary<string, BackoffState> _backoffStates = new();
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    private class BackoffState
    {
        public int AttemptCount { get; set; }
        public DateTime NextRetryUtc { get; set; }
    }

    public AutoconnectManager(PeerManager peerManager, Func<ConnectionProfile, Task> onConnectAttempt)
    {
        _peerManager = peerManager;
        _onConnectAttempt = onConnectAttempt;
    }

    public void Start()
    {
        if (_cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _monitorTask = MonitorAutoconnectAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(5));
        _cts?.Dispose();
        _cts = null;
        _monitorTask = null;
    }

    private async Task MonitorAutoconnectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var autoConnectProfiles = _peerManager.GetAutoConnectProfiles();

                foreach (var profile in autoConnectProfiles)
                {
                    if (!_backoffStates.ContainsKey(profile.Id))
                    {
                        _backoffStates[profile.Id] = new BackoffState { NextRetryUtc = DateTime.UtcNow };
                    }

                    var state = _backoffStates[profile.Id];
                    if (DateTime.UtcNow >= state.NextRetryUtc)
                    {
                        await _onConnectAttempt(profile);
                        state.AttemptCount++;

                        // Exponential backoff: 5s, 15s, 60s, then steady 60s
                        var delaySeconds = state.AttemptCount switch
                        {
                            1 => 5,
                            2 => 15,
                            _ => 60
                        };

                        state.NextRetryUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                    }
                }

                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Log and continue
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    public void ResetBackoff(string profileId)
    {
        if (_backoffStates.ContainsKey(profileId))
        {
            _backoffStates[profileId].AttemptCount = 0;
            _backoffStates[profileId].NextRetryUtc = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
