using System.Runtime.InteropServices;

namespace ClipboardSyncApp.Core;

public sealed class ClipboardWatcher
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const string ExcludeClipboardFormat = "ExcludeClipboardContentFromMonitorProcessing";

    private readonly Func<string, Task>? _textHandler;
    private readonly Func<ClipboardPayload, Task>? _payloadHandler;
    private readonly IntPtr _handle;

    public ClipboardWatcher(IntPtr handle, Func<string, Task>? textHandler = null, Func<ClipboardPayload, Task>? payloadHandler = null)
    {
        _handle = handle;
        _textHandler = textHandler;
        _payloadHandler = payloadHandler;
    }

    public void Register()
    {
        NativeMethods.AddClipboardFormatListener(_handle);
    }

    public void Unregister()
    {
        NativeMethods.RemoveClipboardFormatListener(_handle);
    }

    public bool IsTextAvailable()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch
        {
            return false;
        }
    }

    public string ReadText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool IsImageAvailable()
    {
        try
        {
            return Clipboard.ContainsImage();
        }
        catch
        {
            return false;
        }
    }

    public bool IsFileListAvailable()
    {
        try
        {
            var files = Clipboard.GetFileDropList();
            return files != null && files.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool IsExcludedFromMonitoring()
    {
        try
        {
            return Clipboard.ContainsData(ExcludeClipboardFormat);
        }
        catch
        {
            return false;
        }
    }

    public async Task NotifyTextChangedAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_textHandler != null)
        {
            await _textHandler(text);
        }

        if (_payloadHandler != null)
        {
            var payload = new ClipboardPayload
            {
                Kind = ClipboardPayloadKind.Text,
                SessionId = string.Empty,
                MessageId = Guid.NewGuid().ToString("N"),
                Text = text
            };
            await _payloadHandler(payload);
        }
    }

    public async Task NotifyPayloadChangedAsync(ClipboardPayload payload)
    {
        if (_payloadHandler != null)
        {
            await _payloadHandler(payload);
        }
    }

    public ClipboardPayload? CaptureCurrentClipboard(string sessionId)
    {
        if (IsExcludedFromMonitoring())
        {
            return null;
        }

        try
        {
            if (IsImageAvailable())
            {
                return CaptureImage(sessionId);
            }

            if (IsFileListAvailable())
            {
                return CaptureFileList(sessionId);
            }

            if (IsTextAvailable())
            {
                var text = ReadText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new ClipboardPayload
                    {
                        Kind = ClipboardPayloadKind.Text,
                        SessionId = sessionId,
                        MessageId = Guid.NewGuid().ToString("N"),
                        Text = text
                    };
                }
            }
        }
        catch
        {
            // Silently ignore clipboard access errors
        }

        return null;
    }

    private ClipboardPayload? CaptureImage(string sessionId)
    {
        try
        {
            var image = Clipboard.GetImage();
            if (image == null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = ms.ToArray();

            const int MaxImageSize = 25 * 1024 * 1024; // 25MB
            if (bytes.Length > MaxImageSize)
            {
                return null; // Reject oversized images
            }

            return new ClipboardPayload
            {
                Kind = ClipboardPayloadKind.Image,
                SessionId = sessionId,
                MessageId = Guid.NewGuid().ToString("N"),
                ImageBase64 = Convert.ToBase64String(bytes)
            };
        }
        catch
        {
            return null;
        }
    }

    private ClipboardPayload? CaptureFileList(string sessionId)
    {
        try
        {
            var files = Clipboard.GetFileDropList();
            if (files == null || files.Count == 0)
            {
                return null;
            }

            var firstFile = files[0];
            if (!File.Exists(firstFile))
            {
                return null;
            }

            var fileInfo = new FileInfo(firstFile);
            return new ClipboardPayload
            {
                Kind = ClipboardPayloadKind.FileRef,
                SessionId = sessionId,
                MessageId = Guid.NewGuid().ToString("N"),
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                LocalPath = firstFile
            };
        }
        catch
        {
            return null;
        }
    }

    public bool IsClipboardUpdate(Message m)
    {
        return m.Msg == WM_CLIPBOARDUPDATE;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
