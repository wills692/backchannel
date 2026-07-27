using BackChannel.Core.Crypto;
using Xunit;

namespace BackChannel.Core.Tests.Crypto;

public sealed class FingerprintTests
{
    [Fact]
    public void PublicKeyRoundTripPreservesFingerprint()
    {
        using var identity = IdentityKeyPair.Create(2048);

        var parsed = PublicIdentity.Parse(identity.PublicIdentity.EncodedKey);

        Assert.Equal(identity.Fingerprint, parsed.Fingerprint);
        Assert.Equal(64, parsed.Fingerprint.Value.Length);
    }

    [Fact]
    public void FingerprintNormalizesHexToLowercase()
    {
        var fingerprint = new Fingerprint(new string('A', Fingerprint.HexLength));

        Assert.Equal(new string('a', Fingerprint.HexLength), fingerprint.Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void MalformedFingerprintIsRejected(string value)
    {
        Assert.Throws<FormatException>(() => new Fingerprint(value));
    }

    [Fact]
    public void EmptyFingerprintIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Fingerprint(string.Empty));
    }
}
