using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.UI;

public sealed class HistoryForm : Form
{
    private readonly ListBox _historyList;
    private readonly TextBox _searchBox;
    private readonly Button _recopyButton;
    private readonly Button _clearButton;
    private readonly Label _statusLabel;
    private readonly ClipboardHistoryStore _historyStore;
    private List<(DateTime, string, string, string)> _allEntries = new();

    public HistoryForm()
    {
        Text = "Clipboard History";
        Width = 600;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;

        _historyStore = new ClipboardHistoryStore();

        // Search panel
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
        var searchLabel = new Label { Text = "Search:", Left = 5, Top = 10, AutoSize = true };
        _searchBox = new TextBox { Left = 60, Top = 8, Width = 300 };
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_searchBox);

        // History list
        _historyList = new ListBox { Dock = DockStyle.Top, Height = 300 };

        // Status label
        _statusLabel = new Label { Text = "Items: 0", Dock = DockStyle.Top, Height = 25, AutoSize = false };

        // Button panel
        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _recopyButton = new Button { Text = "Re-copy", Width = 100 };
        _clearButton = new Button { Text = "Clear History", Width = 120 };

        buttonPanel.Controls.Add(_recopyButton);
        buttonPanel.Controls.Add(_clearButton);

        Controls.Add(buttonPanel);
        Controls.Add(_statusLabel);
        Controls.Add(_historyList);
        Controls.Add(searchPanel);

        LoadHistory();

        _searchBox.TextChanged += (_, _) => FilterHistory();
        _recopyButton.Click += (_, _) => RecopySelected();
        _clearButton.Click += (_, _) => ClearHistory();
    }

    private void LoadHistory()
    {
        _allEntries = _historyStore.GetRecent(200);
        FilterHistory();
    }

    private void FilterHistory()
    {
        var searchTerm = _searchBox.Text.ToLowerInvariant();
        var filtered = string.IsNullOrWhiteSpace(searchTerm)
            ? _allEntries
            : _allEntries.Where(e => e.Item2.ToLowerInvariant().Contains(searchTerm)).ToList();

        _historyList.Items.Clear();
        foreach (var (timestamp, preview, kind, source) in filtered)
        {
            var truncated = preview.Length > 50 ? preview.Substring(0, 47) + "..." : preview;
            _historyList.Items.Add($"[{timestamp:g}] {kind} from {source}: {truncated}");
        }

        _statusLabel.Text = $"Items: {filtered.Count}";
    }

    private void RecopySelected()
    {
        if (_historyList.SelectedIndex < 0)
        {
            return;
        }

        var searchTerm = _searchBox.Text.ToLowerInvariant();
        var filtered = string.IsNullOrWhiteSpace(searchTerm)
            ? _allEntries
            : _allEntries.Where(e => e.Item2.ToLowerInvariant().Contains(searchTerm)).ToList();

        if (_historyList.SelectedIndex < filtered.Count)
        {
            var (_, preview, _, _) = filtered[_historyList.SelectedIndex];
            try
            {
                Clipboard.SetText(preview);
                MessageBox.Show(this, "Copied to clipboard.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to copy: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ClearHistory()
    {
        if (MessageBox.Show(this, "Clear all history? This cannot be undone.", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            _allEntries.Clear();
            _historyList.Items.Clear();
            _statusLabel.Text = "Items: 0";
        }
    }
}
