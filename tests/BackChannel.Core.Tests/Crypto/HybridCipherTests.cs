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
        Assert.Equal(4, message.ParticipantFingerprints.Count);
        Assert.Contains(sender.Fingerprint.Value, message.ParticipantFingerprints);
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
    public void NamedGroupMetadataIsSignedAndValidated()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var alice = IdentityKeyPair.Create(2048);
        using var bob = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "signed membership",
            Guid.NewGuid(),
            "Router Crew",
            sender,
            [alice.PublicIdentity, bob.PublicIdentity]);

        Assert.Equal("Router Crew", message.ConversationName);
        Assert.Equal(
            new[]
            {
                sender.Fingerprint.Value,
                alice.Fingerprint.Value,
                bob.Fingerprint.Value,
            }.Order(StringComparer.Ordinal),
            message.ParticipantFingerprints);

        var renamed = message with
        {
            ConversationName = "Impostor Crew",
        };
        var missingMember = message with
        {
            ParticipantFingerprints =
                message.ParticipantFingerprints.Skip(1).ToArray(),
        };

        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(renamed, sender.PublicIdentity, alice));
        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(missingMember, sender.PublicIdentity, alice));
    }

    [Fact]
    public void ResignedInconsistentParticipantMetadataIsRejected()
    {
        using var sender = IdentityKeyPair.Create(2048);
        using var alice = IdentityKeyPair.Create(2048);
        using var bob = IdentityKeyPair.Create(2048);
        var message = HybridCipher.Encrypt(
            "complete membership",
            Guid.NewGuid(),
            "Router Crew",
            sender,
            [alice.PublicIdentity, bob.PublicIdentity]);
        var inconsistent = message with
        {
            ParticipantFingerprints =
                message.ParticipantFingerprints
                    .Where(value => value != bob.Fingerprint.Value)
                    .ToArray(),
            Signature = string.Empty,
        };
        var payload =
            ChatMessageAuthenticator.CreateSignaturePayload(inconsistent);
        inconsistent = inconsistent with
        {
            Signature = Convert.ToBase64String(sender.SignData(payload)),
        };

        Assert.Throws<CryptographicException>(() =>
            HybridCipher.Decrypt(
                inconsistent,
                sender.PublicIdentity,
                alice));
    }

    [Fact]
    public void SenderCannotBeIncludedAsARecipient()
    {
        using var sender = IdentityKeyPair.Create(2048);

        Assert.Throws<ArgumentException>(() =>
            HybridCipher.Encrypt(
                "self",
                Guid.NewGuid(),
                sender,
                [sender.PublicIdentity]));
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
