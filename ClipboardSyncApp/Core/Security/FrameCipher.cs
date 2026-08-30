using System.Security.Cryptography;
using System.Text;

namespace ClipboardSyncApp.Core.Security;

public sealed class FrameCipher
{
    private const int TagSize = 16; // 128 bits
    private const int NonceSizeBytes = 12; // 96 bits for GCM

    public static string EncryptFrame(string plainText, string key)
    {
        if (string.IsNullOrWhiteSpace(plainText) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        try
        {
            var keyBytes = DeriveKey(key);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            using var aes = new AesGcm(keyBytes, TagSize);
            var nonce = new byte[NonceSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }

            var ciphertext = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            aes.Encrypt(nonce, plainBytes, ciphertext, tag);

            // Format: nonce + ciphertext + tag
            var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

            return Convert.ToBase64String(result);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string DecryptFrame(string cipherText, string key)
    {
        if (string.IsNullOrWhiteSpace(cipherText) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        try
        {
            var keyBytes = DeriveKey(key);
            var allBytes = Convert.FromBase64String(cipherText);

            if (allBytes.Length < NonceSizeBytes + TagSize)
            {
                return string.Empty;
            }

            var nonce = new byte[NonceSizeBytes];
            var ciphertextLen = allBytes.Length - NonceSizeBytes - TagSize;
            var ciphertext = new byte[ciphertextLen];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(allBytes, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(allBytes, NonceSizeBytes, ciphertext, 0, ciphertextLen);
            Buffer.BlockCopy(allBytes, NonceSizeBytes + ciphertextLen, tag, 0, TagSize);

            using var aes = new AesGcm(keyBytes, TagSize);
            var plaintext = new byte[ciphertextLen];

            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] DeriveKey(string key)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
    }
}
