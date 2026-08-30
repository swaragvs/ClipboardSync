namespace ClipboardSyncApp.Core;

public interface IClipboardService
{
    bool HasText();
    string GetText();
    void SetText(string text);

    bool HasImage();
    byte[]? GetImageBytes();
    void SetImageBytes(byte[] pngBytes);

    bool HasRtf();
    string? GetRtf();
    void SetRtf(string rtfText);
}
