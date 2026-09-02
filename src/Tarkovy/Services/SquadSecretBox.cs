using System.Security.Cryptography;
using System.Text;

namespace Tarkovy.Services;

/// <summary>
/// AES-256-GCM for values baked into the Release exe.
/// This hides URL/key from a casual strings dump. It is not a substitute for
/// the server-side squad key — the decrypt material still ships in the binary.
/// </summary>
internal static class SquadSecretBox
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] Seal(string plaintext)
    {
        var pt = Encoding.UTF8.GetBytes(plaintext ?? "");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[pt.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(DeriveKey(), TagSize);
        gcm.Encrypt(nonce, pt, cipher, tag);

        var blob = new byte[1 + NonceSize + TagSize + cipher.Length];
        blob[0] = Version;
        Buffer.BlockCopy(nonce, 0, blob, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, 1 + NonceSize + TagSize, cipher.Length);
        return blob;
    }

    public static string Open(byte[] blob)
    {
        if (blob is not { Length: > 1 + NonceSize + TagSize } || blob[0] != Version)
            return "";

        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        var cipher = blob.AsSpan(1 + NonceSize + TagSize);
        var pt = new byte[cipher.Length];
        using var gcm = new AesGcm(DeriveKey(), TagSize);
        gcm.Decrypt(nonce, cipher, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    public static string TryOpen(byte[]? blob)
    {
        if (blob is null || blob.Length == 0)
            return "";
        try
        {
            return Open(blob);
        }
        catch (CryptographicException)
        {
            return "";
        }
    }

    internal static byte[] DeriveKey()
    {
        var a = MaterialA();
        var b = MaterialB();
        var c = SHA256.HashData(Encoding.UTF8.GetBytes(typeof(SquadHost).FullName + "\u001eAnomaly Labs"));
        var mix = new byte[a.Length + b.Length + c.Length];
        Buffer.BlockCopy(a, 0, mix, 0, a.Length);
        Buffer.BlockCopy(b, 0, mix, a.Length, b.Length);
        Buffer.BlockCopy(c, 0, mix, a.Length + b.Length, c.Length);
        return SHA256.HashData(mix);
    }

    // Split so a single 32-byte literal is not sitting next to the ciphertext.
    private static byte[] MaterialA() =>
    [
        0x6B, 0xE2, 0x1A, 0x94, 0xC7, 0x50, 0x3D, 0x8F,
        0x22, 0xB9, 0x07, 0x61, 0xDE, 0x4C, 0xA8, 0x35
    ];

    private static byte[] MaterialB() =>
    [
        0x19, 0xC8, 0xF3, 0x0E, 0x77, 0xA1, 0x5B, 0xD4,
        0x88, 0x2F, 0x46, 0x90, 0x13, 0xEA, 0x6D, 0xB2
    ];
}
