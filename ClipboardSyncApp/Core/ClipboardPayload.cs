namespace ClipboardSyncApp.Core;

public enum MessageType : byte
{
    // Control / Handshake
    Handshake = 0x01,
    HandshakeAck = 0x02,
    Ping = 0x03,
    Pong = 0x04,
    Ack = 0x05,

    // Clipboard Content
    ClipboardText = 0x10,
    ClipboardImage = 0x11,
    ClipboardRtf = 0x12,

    // File Transfer
    FileOffer = 0x20,
    FileAccept = 0x21,
    FileReject = 0x22,
    FileChunk = 0x23,
    FileComplete = 0x24,
    FileCancel = 0x25
}

public sealed class ClipboardPayload
{
    public MessageType Type { get; set; } = MessageType.ClipboardText;
    public string SessionId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string PeerId { get; set; } = string.Empty;
    public string OriginPeerId { get; set; } = string.Empty;
    public string OriginSessionId { get; set; } = string.Empty;
    public ulong SequenceNumber { get; set; }
    public long TimestampUtc { get; set; } = DateTime.UtcNow.Ticks;
    public string ContentHash { get; set; } = string.Empty;
    public byte ContentType { get; set; }
    public byte HopCount { get; set; }
    public uint Capabilities { get; set; } = 0x0F;
    public byte[]? Challenge { get; set; }
    public byte[]? Authenticator { get; set; }

    // Content fields
    public string? Text { get; set; }
    public string? RtfText { get; set; }
    public byte[]? ImageBytes { get; set; }

    // File transfer protocol fields
    public string? TransferId { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? SHA256 { get; set; }
    public long ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public byte[]? ChunkData { get; set; }
    public string? RejectReason { get; set; }
}
