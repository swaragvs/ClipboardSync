using System.Security.Cryptography;
using System.Text;

namespace ClipboardSyncApp.Core.Security;

public sealed class HandshakeService
{
    private const int PskLength = 32; // 256 bits

    public string GeneratePreSharedKey()
    {
        var bytes = new byte[PskLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes);
    }

    public string GeneratePairingCode(string psk)
    {
        if (string.IsNullOrWhiteSpace(psk))
        {
            return string.Empty;
        }

        // Generate a short human-verifiable code from PSK
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(psk));
        var shortHash = Convert.ToHexString(hash.Take(4).ToArray());
        return shortHash.ToUpper();
    }

    public bool VerifyPairingCode(string psk, string pairingCode)
    {
        var expected = GeneratePairingCode(psk);
        return string.Equals(expected, pairingCode, StringComparison.OrdinalIgnoreCase);
    }

    public bool Validate(string? peerId, string? sharedKey)
    {
        return !string.IsNullOrWhiteSpace(peerId) && !string.IsNullOrWhiteSpace(sharedKey);
    }
}
