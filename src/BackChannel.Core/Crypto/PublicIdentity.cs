using System.Security.Cryptography;

namespace BackChannel.Core.Crypto;

public sealed class PublicIdentity : IEquatable<PublicIdentity>
{
    private readonly byte[] _subjectPublicKeyInfo;

    private PublicIdentity(byte[] subjectPublicKeyInfo)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);

        if (bytesRead != subjectPublicKeyInfo.Length)
        {
            throw new CryptographicException("The public key contains trailing data.");
        }

        if (rsa.KeySize < IdentityKeyPair.MinimumKeySize)
        {
            throw new CryptographicException(
                $"RSA public keys must be at least {IdentityKeyPair.MinimumKeySize} bits.");
        }

        _subjectPublicKeyInfo = subjectPublicKeyInfo;
        Fingerprint = Fingerprint.FromSubjectPublicKeyInfo(_subjectPublicKeyInfo);
    }

    public Fingerprint Fingerprint { get; }

    public string EncodedKey => Convert.ToBase64String(_subjectPublicKeyInfo);

    public static PublicIdentity Parse(string encodedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedKey);
        return new PublicIdentity(Convert.FromBase64String(encodedKey));
    }

    internal static PublicIdentity FromSubjectPublicKeyInfo(ReadOnlySpan<byte> publicKey) =>
        new(publicKey.ToArray());

    internal byte[] WrapKey(ReadOnlySpan<byte> key)
    {
        using var rsa = CreateRsa();
        return rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
    }

    internal bool VerifySignature(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        using var rsa = CreateRsa();
        return rsa.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
    }

    public bool Equals(PublicIdentity? other) =>
        other is not null && Fingerprint == other.Fingerprint;

    public override bool Equals(object? obj) => obj is PublicIdentity other && Equals(other);

    public override int GetHashCode() => Fingerprint.GetHashCode();

    private RSA CreateRsa()
    {
        var rsa = RSA.Create();

        try
        {
            rsa.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out _);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }
}
