using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipboardSyncApp.Core;

namespace ClipboardSyncApp.Platform.Windows;

public sealed class WindowsClipboardService : IClipboardService, IDisposable
{
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly Thread _staThread;
    private readonly CancellationTokenSource _cts = new();

    public WindowsClipboardService()
    {
        _staThread = new Thread(StaLoop)
        {
            IsBackground = true,
            Name = "ClipboardSync_STA_Worker"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    public bool HasText()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() => Clipboard.ContainsText()));
    }

    public string GetText()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty)) ?? string.Empty;
    }

    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        InvokeOnSta(() => ExecuteWithRetry(() => Clipboard.SetText(text)));
    }

    public bool HasImage()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() => Clipboard.ContainsImage()));
    }

    public byte[]? GetImageBytes()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() =>
        {
            var image = Clipboard.GetImage();
            if (image == null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }));
    }

    public void SetImageBytes(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return;
        }

        InvokeOnSta(() => ExecuteWithRetry(() =>
        {
            using var ms = new MemoryStream(pngBytes);
            using var image = Image.FromStream(ms);
            Clipboard.SetImage(image);
        }));
    }

    public bool HasRtf()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() => Clipboard.ContainsData(DataFormats.Rtf)));
    }

    public string? GetRtf()
    {
        return InvokeOnSta(() => ExecuteWithRetry(() =>
        {
            if (Clipboard.ContainsData(DataFormats.Rtf))
            {
                return Clipboard.GetData(DataFormats.Rtf) as string;
            }
            return null;
        }));
    }

    public void SetRtf(string rtfText)
    {
        if (string.IsNullOrEmpty(rtfText))
        {
            return;
        }

        InvokeOnSta(() => ExecuteWithRetry(() =>
        {
            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.Rtf, rtfText);
            dataObj.SetData(DataFormats.Text, rtfText);
            Clipboard.SetDataObject(dataObj, true);
        }));
    }

    private T InvokeOnSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread == _staThread)
        {
            return action();
        }

        if (_cts.IsCancellationRequested)
        {
            return default!;
        }

        var tcs = new TaskCompletionSource<T>();
        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    private void InvokeOnSta(Action action)
    {
        InvokeOnSta<object?>(() =>
        {
            action();
            return null;
        });
    }

    private void StaLoop()
    {
        try
        {
            foreach (var action in _workQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    action();
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static T ExecuteWithRetry<T>(Func<T> action)
    {
        const int maxRetries = 4;
        const int delayMs = 30;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                return action();
            }
            catch (ExternalException)
            {
                if (i == maxRetries)
                {
                    break;
                }
                Thread.Sleep(delayMs * (i + 1));
            }
            catch
            {
                break;
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

    public void Dispose()
    {
        _cts.Cancel();
        _workQueue.CompleteAdding();
        _cts.Dispose();
    }
}
