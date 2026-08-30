using System.Runtime.InteropServices;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.UI;

namespace ClipboardSyncApp;

static class Program
{
    private static Mutex? SingleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var createdNew = false;
        SingleInstanceMutex = new Mutex(true, "Local\\ClipboardSyncApp_SingleInstance", out createdNew);

        if (!createdNew)
        {
            RestoreExistingInstance();
            return;
        }

        var settings = AppSettings.Load();
        settings.ApplyStartupSetting();

        var form = new Form1(settings);
        var trayContext = new TrayContext(form, settings, form.Engine);

        if (args.Contains("--background") || settings.StartMinimized)
        {
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Hide();
        }

        form.SetTrayContext(trayContext);
        Application.Run(form);
    }

    private static void RestoreExistingInstance()
    {
        var windowHandle = NativeMethods.FindWindow(null, "Clipboard Sync");
        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(windowHandle, 9);
            NativeMethods.SetForegroundWindow(windowHandle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}