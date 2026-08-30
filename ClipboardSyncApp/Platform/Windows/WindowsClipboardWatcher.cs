using System.Runtime.InteropServices;
using ClipboardSyncApp.Core;

namespace ClipboardSyncApp.Platform.Windows;

public sealed class WindowsClipboardWatcher : IClipboardWatcher
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly IntPtr _windowHandle;
    public event EventHandler? ClipboardChanged;

    public WindowsClipboardWatcher(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public void Register()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.AddClipboardFormatListener(_windowHandle);
        }
    }

    public void Unregister()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_windowHandle);
        }
    }

    public bool HandleWndProc(int msg)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
