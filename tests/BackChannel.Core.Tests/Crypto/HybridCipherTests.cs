using System.Security.Cryptography;
using BackChannel.Core.Crypto;
using BackChannel.Core.Protocol;
using Xunit;

namespace BackChannel.Core.Tests.Crypto;

public sealed class HybridCipherTests
{
    [Fact]
    public void ThreeRecipientsCanDecryptAndOutsiderCannot()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var alice = IdentityKeyPair.Create(2048);
        using var bob = IdentityKeyPair.Create(2048);
        using var dana = IdentityKeyPair.Create(2048);
        using var outsider = IdentityKeyPair.Create(2048);
        var conversationId = Guid.NewGuid();

        var message = HybridCipher.Encrypt(
            "meet by the router",
            conversationId,
            sender,
            [alice.PublicIdentity, bob.PublicIdentity, dana.PublicIdentity]);

        Assert.Equal(3, message.RecipientKeys.Count);
        Assert.Equal(conversationId, message.ConversationId);
        Assert.Equal("meet by the router", HybridCipher.Decrypt(message, sender.PublicIdentity, alice));
        Assert.Equal("meet by the router", HybridCipher.Decrypt(message, sender.PublicIdentity, bob));
        Assert.Equal("meet by the router", HybridCipher.Decrypt(message, sender.PublicIdentity, dana));
        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(message, sender.PublicIdentity, outsider));
    }

    [Fact]
    public void CiphertextTamperingIsRejectedBySignature()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var recipient = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "authentic",
            Guid.NewGuid(),
            sender,
            [recipient.PublicIdentity]);
        var tampered = message with
        {
            Ciphertext = FlipFirstByte(message.Ciphertext),
        };

        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(tampered, sender.PublicIdentity, recipient));
    }

    [Fact]
    public void InvalidSignatureIsRejected()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var recipient = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "signed",
            Guid.NewGuid(),
            sender,
            [recipient.PublicIdentity]);
        var tampered = message with
        {
            Signature = FlipFirstByte(message.Signature),
        };

        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(tampered, sender.PublicIdentity, recipient));
    }

    [Fact]
    public void GcmRejectsTamperingEvenWhenMessageIsResigned()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var recipient = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "authenticated encryption",
            Guid.NewGuid(),
            sender,
            [recipient.PublicIdentity]);
        var tampered = message with
        {
            Ciphertext = FlipFirstByte(message.Ciphertext),
            Signature = string.Empty,
        };
        var signaturePayload = ChatMessageAuthenticator.CreateSignaturePayload(tampered);
        tampered = tampered with
        {
            Signature = Convert.ToBase64String(sender.SignData(signaturePayload)),
        };

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            HybridCipher.Decrypt(tampered, sender.PublicIdentity, recipient));
    }

    [Fact]
    public void MetadataTamperingIsRejected()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var recipient = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "same bytes, different conversation",
            Guid.NewGuid(),
            sender,
            [recipient.PublicIdentity]);
        var tampered = message with
        {
            ConversationId = Guid.NewGuid(),
        };

        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(tampered, sender.PublicIdentity, recipient));
    }

    [Fact]
    public void DuplicateRecipientsAreRejected()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var recipient = IdentityKeyPair.Create(2048);

        Assert.Throws<ArgumentException>(() =>
            HybridCipher.Encrypt(
                "duplicate",
                Guid.NewGuid(),
                sender,
                [recipient.PublicIdentity, recipient.PublicIdentity]));
    }

    private static string FlipFirstByte(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        bytes[0] ^= 0x01;
        return Convert.ToBase64String(bytes);
    }
}
