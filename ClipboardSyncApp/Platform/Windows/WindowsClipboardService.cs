using System.Drawing;
using System.Drawing.Imaging;
using ClipboardSyncApp.Core;

namespace ClipboardSyncApp.Platform.Windows;

public sealed class WindowsClipboardService : IClipboardService
{
    private static readonly int[] RetryDelaysMs = new[] { 10, 25, 50, 100 };

    public bool HasText()
    {
        return ExecuteWithRetry(() => Clipboard.ContainsText());
    }

    public string GetText()
    {
        return ExecuteWithRetry(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty) ?? string.Empty;
    }

    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ExecuteWithRetry(() => Clipboard.SetText(text));
    }

    public bool HasImage()
    {
        return ExecuteWithRetry(() => Clipboard.ContainsImage());
    }

    public byte[]? GetImageBytes()
    {
        return ExecuteWithRetry(() =>
        {
            var image = Clipboard.GetImage();
            if (image == null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        });
    }

    public void SetImageBytes(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return;
        }

        ExecuteWithRetry(() =>
        {
            using var ms = new MemoryStream(pngBytes);
            using var image = Image.FromStream(ms);
            Clipboard.SetImage(image);
        });
    }

    public bool HasRtf()
    {
        return ExecuteWithRetry(() => Clipboard.ContainsData(DataFormats.Rtf));
    }

    public string? GetRtf()
    {
        return ExecuteWithRetry(() =>
        {
            if (Clipboard.ContainsData(DataFormats.Rtf))
            {
                return Clipboard.GetData(DataFormats.Rtf) as string;
            }
            return null;
        });
    }

    public void SetRtf(string rtfText)
    {
        if (string.IsNullOrEmpty(rtfText))
        {
            return;
        }

        ExecuteWithRetry(() =>
        {
            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.Rtf, rtfText);
            dataObj.SetData(DataFormats.Text, rtfText); // Plain text fallback
            Clipboard.SetDataObject(dataObj, true);
        });
    }

    private static T ExecuteWithRetry<T>(Func<T> action)
    {
        for (int i = 0; i <= RetryDelaysMs.Length; i++)
        {
            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    return action();
                }

                T result = default!;
                Exception? threadException = null;
                var staThread = new Thread(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        threadException = ex;
                    }
                });

                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();

                if (threadException == null)
                {
                    return result;
                }
            }
            catch
            {
            }

            if (i < RetryDelaysMs.Length)
            {
                Thread.Sleep(RetryDelaysMs[i]);
            }
        }

        return default!;
    }

    private static void ExecuteWithRetry(Action action)
    {
        ExecuteWithRetry<object?>(() =>
        {
            action();
            return null;
        });
    }
}
