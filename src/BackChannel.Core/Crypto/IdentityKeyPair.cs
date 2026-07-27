using System.Security.Cryptography;

namespace BackChannel.Core.Crypto;

public sealed class IdentityKeyPair : IDisposable
{
    public const int DefaultKeySize = 3072;
    public const int MinimumKeySize = 2048;

    private readonly RSA _rsa;
    private bool _disposed;

    private IdentityKeyPair(RSA rsa)
    {
        _rsa = rsa;
        PublicIdentity = PublicIdentity.FromSubjectPublicKeyInfo(
            _rsa.ExportSubjectPublicKeyInfo());
    }

    public PublicIdentity PublicIdentity { get; }

    public Fingerprint Fingerprint => PublicIdentity.Fingerprint;

    public static IdentityKeyPair Create(int keySize = DefaultKeySize)
    {
        if (keySize < MinimumKeySize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keySize),
                keySize,
                $"RSA identity keys must be at least {MinimumKeySize} bits.");
        }

        var rsa = RSA.Create();

        try
        {
            rsa.KeySize = keySize;
            return new IdentityKeyPair(rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    internal byte[] UnwrapKey(ReadOnlySpan<byte> wrappedKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
    }

    internal byte[] SignData(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _rsa.SignData(
            data,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _rsa.Dispose();
        _disposed = true;
    }
}
