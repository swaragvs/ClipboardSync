namespace ClipboardSyncApp.Core;

public enum ClipboardPayloadKind
{
    Text,
    Image,
    FileRef,
    Rtf
}

public sealed class ClipboardPayload
{
    public ClipboardPayloadKind Kind { get; set; } = ClipboardPayloadKind.Text;
    public string SessionId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ImageBase64 { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? LocalPath { get; set; }
    public string? RtfText { get; set; }
}
