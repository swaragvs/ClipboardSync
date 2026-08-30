using System.Security.Cryptography;
using System.Text;

namespace ClipboardSyncApp.Core.Security;

public sealed class FrameCipher
{
    public const int HeaderSizeBytes = 14;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    /// <summary>
    /// Derives a 256-bit AES session key using HKDF-SHA256 from PSK and session challenges.
    /// </summary>
    public static byte[] DeriveSessionKey(string preSharedKey, byte[] clientNonce, byte[] serverNonce)
    {
        var ikm = Encoding.UTF8.GetBytes(preSharedKey);
        var salt = new byte[clientNonce.Length + serverNonce.Length];
        Buffer.BlockCopy(clientNonce, 0, salt, 0, clientNonce.Length);
        Buffer.BlockCopy(serverNonce, 0, salt, clientNonce.Length, serverNonce.Length);

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt, Encoding.UTF8.GetBytes("ClipboardSync-v2-AES-GCM-Key"));
    }

    /// <summary>
    /// AES-GCM Encrypt with 14-byte Header AAD authentication.
    /// Header layout: [Version (1B)][Length (4B BE)][MessageType (1B)][SequenceNumber (8B BE)]
    /// </summary>
    public static byte[] EncryptFrame(byte[] payloadBytes, byte[]? sessionKey, ulong sequenceNumber, byte protocolVersion, byte messageType, out byte[] header)
    {
        header = new byte[HeaderSizeBytes];
        header[0] = protocolVersion;
        header[5] = messageType;
        BinaryPrimitives_WriteUInt64BigEndian(header.AsSpan(6, 8), sequenceNumber);

        if (sessionKey == null || sessionKey.Length == 0)
        {
            // Unencrypted envelope: Length = payloadBytes.Length
            BinaryPrimitives_WriteInt32BigEndian(header.AsSpan(1, 4), payloadBytes.Length);
            return payloadBytes;
        }

        var ciphertextLength = payloadBytes.Length;
        var envelopeLength = NonceSizeBytes + TagSizeBytes + ciphertextLength;
        BinaryPrimitives_WriteInt32BigEndian(header.AsSpan(1, 4), envelopeLength);

        // Nonce = Random 96-bit
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[ciphertextLength];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(sessionKey, TagSizeBytes);
        aesGcm.Encrypt(nonce, payloadBytes, ciphertext, tag, header);

        // Output Envelope = [Nonce (12B)][Tag (16B)][Ciphertext (N B)]
        var output = new byte[envelopeLength];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSizeBytes + TagSizeBytes, ciphertextLength);

        return output;
    }

    /// <summary>
    /// AES-GCM Decrypt with 14-byte Header AAD validation.
    /// </summary>
    public static byte[] DecryptFrame(byte[] frameBytes, byte[]? sessionKey, byte[] header)
    {
        if (sessionKey == null || sessionKey.Length == 0)
        {
            return frameBytes;
        }

        if (frameBytes.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new InvalidDataException("Frame ciphertext buffer is too short.");
        }

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertextLength = frameBytes.Length - NonceSizeBytes - TagSizeBytes;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(frameBytes, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(frameBytes, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(frameBytes, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertextLength);

        var decryptedPlaintext = new byte[ciphertextLength];

        using var aesGcm = new AesGcm(sessionKey, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, decryptedPlaintext, header);

        return decryptedPlaintext;
    }

    public static string ProtectSecret(string plainSecret)
    {
        if (string.IsNullOrEmpty(plainSecret))
        {
            return string.Empty;
        }
        var bytes = Encoding.UTF8.GetBytes(plainSecret);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectSecret(string protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return string.Empty;
        }
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedSecret);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void BinaryPrimitives_WriteInt32BigEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    public static int BinaryPrimitives_ReadInt32BigEndian(ReadOnlySpan<byte> source)
    {
        return (source[0] << 24) | (source[1] << 16) | (source[2] << 8) | source[3];
    }

    private static void BinaryPrimitives_WriteUInt64BigEndian(Span<byte> destination, ulong value)
    {
        destination[0] = (byte)(value >> 56);
        destination[1] = (byte)(value >> 48);
        destination[2] = (byte)(value >> 40);
        destination[3] = (byte)(value >> 32);
        destination[4] = (byte)(value >> 24);
        destination[5] = (byte)(value >> 16);
        destination[6] = (byte)(value >> 8);
        destination[7] = (byte)value;
    }

    public static ulong BinaryPrimitives_ReadUInt64BigEndian(ReadOnlySpan<byte> source)
    {
        return ((ulong)source[0] << 56) |
               ((ulong)source[1] << 48) |
               ((ulong)source[2] << 40) |
               ((ulong)source[3] << 32) |
               ((ulong)source[4] << 24) |
               ((ulong)source[5] << 16) |
               ((ulong)source[6] << 8) |
               (ulong)source[7];
    }
}
