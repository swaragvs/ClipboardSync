using ClipboardSyncApp.Core;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.UI;

public sealed class PeerManagerForm : Form
{
    private readonly ListBox _peerList;
    private readonly ListBox _discoveredList;
    private readonly Button _addButton;
    private readonly Button _connectButton;
    private readonly Button _removeButton;
    private readonly Button _promoteDiscoveredButton;
    private readonly Button _refreshDiscoveryButton;
    private readonly CheckBox _autoConnectCheckBox;
    private readonly DiscoveryService _discoveryService;
    private readonly ClipboardSyncEngine? _engine;

    public PeerManagerForm(ClipboardSyncEngine? engine = null)
    {
        _engine = engine;
        Text = "Peer Manager";
        Width = 800;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;

        _discoveryService = new DiscoveryService();

        // Left panel: saved peers
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 400 };
        var leftLabel = new Label { Text = "Saved Peers", Dock = DockStyle.Top, Height = 20 };
        _peerList = new ListBox { Dock = DockStyle.Top, Height = 180 };
        _autoConnectCheckBox = new CheckBox { Text = "Auto Connect", Dock = DockStyle.Top, Height = 25 };

        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown };
        _addButton = new Button { Text = "Add", Width = 100 };
        _connectButton = new Button { Text = "Connect", Width = 100 };
        _removeButton = new Button { Text = "Remove", Width = 100 };

        leftButtonPanel.Controls.Add(_addButton);
        leftButtonPanel.Controls.Add(_connectButton);
        leftButtonPanel.Controls.Add(_removeButton);

        leftPanel.Controls.Add(leftButtonPanel);
        leftPanel.Controls.Add(_peerList);
        leftPanel.Controls.Add(leftLabel);

        // Right panel: discovered peers
        var rightPanel = new Panel { Dock = DockStyle.Right, Width = 350 };
        var rightLabel = new Label { Text = "Discovered Peers", Dock = DockStyle.Top, Height = 20 };
        _discoveredList = new ListBox { Dock = DockStyle.Top, Height = 220 };

        var rightButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown };
        _refreshDiscoveryButton = new Button { Text = "Refresh", Width = 100 };
        _promoteDiscoveredButton = new Button { Text = "Add to Saved", Width = 100 };

        rightButtonPanel.Controls.Add(_refreshDiscoveryButton);
        rightButtonPanel.Controls.Add(_promoteDiscoveredButton);

        rightPanel.Controls.Add(rightButtonPanel);
        rightPanel.Controls.Add(_discoveredList);
        rightPanel.Controls.Add(rightLabel);

        Controls.Add(leftPanel);
        Controls.Add(rightPanel);

        LoadPeers();
        RefreshDiscovery();

        _addButton.Click += (_, _) => AddProfile();
        _connectButton.Click += async (_, _) => await ConnectSelectedAsync();
        _removeButton.Click += (_, _) => RemoveSelected();
        _autoConnectCheckBox.CheckedChanged += (_, _) => ToggleAutoConnect();
        _refreshDiscoveryButton.Click += (_, _) => RefreshDiscovery();
        _promoteDiscoveredButton.Click += (_, _) => PromoteDiscovered();
        _peerList.SelectedIndexChanged += (_, _) => UpdateAutoConnectCheckbox();
    }

    private void LoadPeers()
    {
        _peerList.Items.Clear();
        var peers = ConnectionStore.Load();
        foreach (var peer in peers)
        {
            var statusStr = peer.IsOnline ? "🟢 Online" : "🔴 Offline";
            var lastSeen = peer.LastConnectedUtc > DateTime.MinValue
                ? $"[{peer.LastConnectedUtc:g}]"
                : "[never]";
            _peerList.Items.Add($"{statusStr} | {peer.Name} ({peer.TailscaleIp}:{peer.Port}) {lastSeen}");
        }
    }

    private void RefreshDiscovery()
    {
        _discoveredList.Items.Clear();
        var candidates = _discoveryService.DiscoverPeerCandidates();
        var savedIps = ConnectionStore.Load().Select(p => p.TailscaleIp).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!savedIps.Contains(candidate))
            {
                _discoveredList.Items.Add(candidate);
            }
        }
    }

    private void AddProfile()
    {
        var profile = new ConnectionProfile
        {
            Name = "New Peer",
            TailscaleIp = "100.64.0.2",
            Port = 5001,
            AutoConnect = false
        };

        ConnectionStore.Upsert(profile);
        LoadPeers();
    }

    private void PromoteDiscovered()
    {
        var item = _discoveredList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        var profile = new ConnectionProfile
        {
            Name = $"Peer {item}",
            TailscaleIp = item,
            Port = 5001,
            AutoConnect = false
        };

        ConnectionStore.Upsert(profile);
        LoadPeers();
        RefreshDiscovery();
    }

    private async Task ConnectSelectedAsync()
    {
        if (_peerList.SelectedIndex < 0)
        {
            return;
        }

        var peers = ConnectionStore.Load();
        if (_peerList.SelectedIndex < peers.Count)
        {
            var peer = peers[_peerList.SelectedIndex];
            _connectButton.Enabled = false;
            try
            {
                if (_engine != null)
                {
                    await _engine.AttemptPeerConnectionAsync(peer);
                }
                else
                {
                    using var testEngine = new ClipboardSyncEngine();
                    await testEngine.AttemptPeerConnectionAsync(peer);
                }

                LoadPeers();
                var status = peer.IsOnline ? "Connection successful!" : $"Connection failed: {peer.LastError}";
                MessageBox.Show(this, status, "Peer Manager Connection", MessageBoxButtons.OK, peer.IsOnline ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                _connectButton.Enabled = true;
            }
        }
    }

    private void RemoveSelected()
    {
        var item = _peerList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        var peers = ConnectionStore.Load();
        if (peers.Count > 0 && _peerList.SelectedIndex >= 0)
        {
            var selected = peers[_peerList.SelectedIndex];
            ConnectionStore.Delete(selected.Id);
            LoadPeers();
            RefreshDiscovery();
        }
    }

    private void UpdateAutoConnectCheckbox()
    {
        if (_peerList.SelectedIndex < 0)
        {
            _autoConnectCheckBox.Checked = false;
            _autoConnectCheckBox.Enabled = false;
            return;
        }

        var peers = ConnectionStore.Load();
        if (_peerList.SelectedIndex < peers.Count)
        {
            _autoConnectCheckBox.Enabled = true;
            _autoConnectCheckBox.Checked = peers[_peerList.SelectedIndex].AutoConnect;
        }
    }

    private void ToggleAutoConnect()
    {
        if (_peerList.SelectedIndex < 0)
        {
            return;
        }

        var peers = ConnectionStore.Load();
        if (_peerList.SelectedIndex < peers.Count)
        {
            var peer = peers[_peerList.SelectedIndex];
            peer.AutoConnect = _autoConnectCheckBox.Checked;
            ConnectionStore.Upsert(peer);
            LoadPeers();
            _peerList.SelectedIndex = Math.Min(_peerList.SelectedIndex, _peerList.Items.Count - 1);
        }
    }
}

