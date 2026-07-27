using System.Security.Cryptography;

namespace BackChannel.Core.Crypto;

public readonly record struct Fingerprint
{
    public const int HexLength = 64;

    public Fingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != HexLength || !value.All(Uri.IsHexDigit))
        {
            throw new FormatException(
                $"A fingerprint must contain exactly {HexLength} hexadecimal characters.");
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public static Fingerprint FromSubjectPublicKeyInfo(ReadOnlySpan<byte> publicKey)
    {
        var hash = SHA256.HashData(publicKey);
        return new Fingerprint(Convert.ToHexStringLower(hash));
    }

    public override string ToString() => Value;
}
