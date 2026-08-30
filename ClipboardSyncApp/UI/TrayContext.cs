using System.Diagnostics;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.Core;

namespace ClipboardSyncApp.UI;

public sealed class TrayContext : IDisposable
{
    private readonly Form _mainForm;
    private readonly AppSettings _settings;
    private readonly ClipboardSyncEngine _engine;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;

    public TrayContext(Form mainForm, AppSettings settings, ClipboardSyncEngine engine)
    {
        _mainForm = mainForm;
        _settings = settings;
        _engine = engine;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Open", null, (_, _) => ShowMainForm());
        _contextMenu.Items.Add("Peer Manager", null, (_, _) => ShowPeerManager());
        _contextMenu.Items.Add("Clipboard History", null, (_, _) => ShowHistory());
        _contextMenu.Items.Add("Pause Sync", null, (_, _) => TogglePause());
        _contextMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "Clipboard Sync"
        };

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
        }
        catch
        {
            // No custom icon present; tray icon will use default app icon.
        }

        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();
    }

    public void ShowBalloon(string text)
    {
        _notifyIcon.ShowBalloonTip(3000, "ClipboardSync", text, ToolTipIcon.Info);
    }

    private void ShowMainForm()
    {
        if (_mainForm.WindowState == FormWindowState.Minimized)
        {
            _mainForm.WindowState = FormWindowState.Normal;
        }

        _mainForm.Show();
        _mainForm.Activate();
        _mainForm.ShowInTaskbar = true;
    }

    private void ShowPeerManager()
    {
        var peerManagerForm = new PeerManagerForm(_engine);
        peerManagerForm.ShowDialog();
    }

    private void ShowHistory()
    {
        var historyForm = new HistoryForm();
        historyForm.ShowDialog();
    }

    private void ShowSettings()
    {
        var settingsForm = new SettingsForm(_settings);
        settingsForm.ShowDialog();
    }

    private void TogglePause()
    {
        var menuItem = _contextMenu.Items[3] as ToolStripMenuItem;
        if (menuItem == null)
        {
            return;
        }

        menuItem.Checked = !menuItem.Checked;
        menuItem.Text = menuItem.Checked ? "Resume Sync" : "Pause Sync";
        ShowBalloon(menuItem.Checked ? "Clipboard sync paused." : "Clipboard sync resumed.");
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _engine.Stop();
        Application.Exit();
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
