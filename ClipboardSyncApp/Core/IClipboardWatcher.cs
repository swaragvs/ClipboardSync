namespace ClipboardSyncApp.Core;

public interface IClipboardWatcher
{
    event EventHandler? ClipboardChanged;
    void Register();
    void Unregister();
}
