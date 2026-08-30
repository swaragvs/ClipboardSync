using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.UI;

namespace ClipboardSyncApp;

static class Program
{
    private const string MutexName = "Local\\ClipboardSyncApp_SingleInstance";
    private const string PipeName = "ClipboardSync_IPC_Pipe";
    private static Mutex? SingleInstanceMutex;
    private static CancellationTokenSource? IpcCts;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var createdNew = false;
        SingleInstanceMutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            SendIpcCommand("OPEN");
            return;
        }

        var settings = AppSettings.Load();

        var mainForm = new MainForm(settings);
        var trayContext = new TrayContext(mainForm, settings, mainForm.Engine);
        mainForm.SetTrayContext(trayContext);

        IpcCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenIpcLoopAsync(mainForm, IpcCts.Token));

        if (args.Contains("--background") || settings.StartMinimized)
        {
            mainForm.WindowState = FormWindowState.Minimized;
            mainForm.ShowInTaskbar = false;
            mainForm.Hide();
        }

        Application.Run(mainForm);

        IpcCts.Cancel();
        SingleInstanceMutex.ReleaseMutex();
        SingleInstanceMutex.Dispose();
    }

    private static async Task ListenIpcLoopAsync(MainForm mainForm, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                var command = await reader.ReadLineAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(command))
                {
                    mainForm.BeginInvoke(new Action(() =>
                    {
                        switch (command.Trim().ToUpperInvariant())
                        {
                            case "OPEN":
                                mainForm.WindowState = FormWindowState.Normal;
                                mainForm.Show();
                                mainForm.ShowInTaskbar = true;
                                mainForm.BringToFront();
                                mainForm.Activate();
                                break;

                            case "EXIT":
                                Application.Exit();
                                break;
                        }
                    }));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private static void SendIpcCommand(string command)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipeClient.Connect(1000);
            using var writer = new StreamWriter(pipeClient, Encoding.UTF8);
            writer.WriteLine(command);
            writer.Flush();
        }
        catch
        {
            // Fallback to Win32 FindWindow if pipe fails
            RestoreViaWin32();
        }
    }

    private static void RestoreViaWin32()
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