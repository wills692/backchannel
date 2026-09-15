using System.Security.Cryptography;
using System.Text;
using BackChannel.Core.Protocol;

namespace BackChannel.Core.Crypto;

public static class HybridCipher
{
    public const int ContentKeySize = 32;
    public const int NonceSize = 12;
    public const int AuthenticationTagSize = 16;

    public static ChatMessage Encrypt(
        string plaintext,
        Guid conversationId,
        IdentityKeyPair sender,
        IEnumerable<PublicIdentity> recipients)
    {
        return Encrypt(
            plaintext,
            conversationId,
            "Conversation",
            sender,
            recipients);
    }

    public static ChatMessage Encrypt(
        string plaintext,
        Guid conversationId,
        string conversationName,
        IdentityKeyPair sender,
        IEnumerable<PublicIdentity> recipients,
        string contentType = ChatMessage.ContentTypes.Chat)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipients);

        var recipientList = recipients.ToArray();
        if (recipientList.Length == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        if (recipientList.Select(static recipient => recipient.Fingerprint).Distinct().Count()
            != recipientList.Length)
        {
            throw new ArgumentException(
                "Recipient fingerprints must be unique.",
                nameof(recipients));
        }

        if (recipientList.Any(
                recipient => recipient.Fingerprint == sender.Fingerprint))
        {
            throw new ArgumentException(
                "The sender cannot also be an encrypted-message recipient.",
                nameof(recipients));
        }

        if (conversationName.Length > ChatMessage.MaximumConversationNameLength)
        {
            throw new ArgumentException(
                $"Conversation names cannot exceed {ChatMessage.MaximumConversationNameLength} characters.",
                nameof(conversationName));
        }

        if (recipientList.Length + 1 > ChatMessage.MaximumParticipantCount)
        {
            throw new ArgumentException(
                $"A conversation cannot exceed {ChatMessage.MaximumParticipantCount} participants.",
                nameof(recipients));
        }

        var participantFingerprints = recipientList
            .Select(static recipient => recipient.Fingerprint.Value)
            .Append(sender.Fingerprint.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var contentKey = RandomNumberGenerator.GetBytes(ContentKeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var authenticationTag = new byte[AuthenticationTagSize];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];

        try
        {
            using (var aes = new AesGcm(contentKey, AuthenticationTagSize))
            {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, authenticationTag);
            }

            var recipientKeys = recipientList
                .Select(recipient => new RecipientKeyEnvelope
                {
                    RecipientFingerprint = recipient.Fingerprint.Value,
                    WrappedKey = Convert.ToBase64String(recipient.WrapKey(contentKey)),
                })
                .ToArray();

            var unsignedMessage = new ChatMessage
            {
                ConversationId = conversationId,
                ConversationName = conversationName,
                ParticipantFingerprints = participantFingerprints,
                SenderFingerprint = sender.Fingerprint.Value,
                ContentType = contentType,
                RecipientKeys = recipientKeys,
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                AuthenticationTag = Convert.ToBase64String(authenticationTag),
                Signature = string.Empty,
            };

            var signaturePayload =
                ChatMessageAuthenticator.CreateSignaturePayload(unsignedMessage);

            try
            {
                return unsignedMessage with
                {
                    Signature = Convert.ToBase64String(sender.SignData(signaturePayload)),
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signaturePayload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public static string Decrypt(
        ChatMessage message,
        PublicIdentity sender,
        IdentityKeyPair recipient)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);

        if (!string.Equals(
                message.SenderFingerprint,
                sender.Fingerprint.Value,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "The claimed sender fingerprint does not match the supplied public key.");
        }

        VerifySignature(message, sender);
        ValidateConversationMetadata(message, recipient);

        var matchingKeys = message.RecipientKeys
            .Where(key => string.Equals(
                key.RecipientFingerprint,
                recipient.Fingerprint.Value,
                StringComparison.Ordinal))
            .ToArray();

        if (matchingKeys.Length != 1)
        {
            throw new CryptographicException(
                "The message must contain exactly one wrapped key for this recipient.");
        }

        byte[] wrappedKey;
        byte[] nonce;
        byte[] ciphertext;
        byte[] authenticationTag;

        try
        {
            wrappedKey = Convert.FromBase64String(matchingKeys[0].WrappedKey);
            nonce = Convert.FromBase64String(message.Nonce);
            ciphertext = Convert.FromBase64String(message.Ciphertext);
            authenticationTag = Convert.FromBase64String(message.AuthenticationTag);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException(
                "The encrypted message contains malformed base64 data.",
                exception);
        }

        if (nonce.Length != NonceSize || authenticationTag.Length != AuthenticationTagSize)
        {
            throw new CryptographicException(
                "The encrypted message has an invalid nonce or authentication tag length.");
        }

        var contentKey = recipient.UnwrapKey(wrappedKey);
        if (contentKey.Length != ContentKeySize)
        {
            CryptographicOperations.ZeroMemory(contentKey);
            throw new CryptographicException("The unwrapped content key has an invalid length.");
        }

        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(contentKey, AuthenticationTagSize);
            aes.Decrypt(nonce, ciphertext, authenticationTag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void VerifySignature(ChatMessage message, PublicIdentity sender)
    {
        byte[] signature;

        try
        {
            signature = Convert.FromBase64String(message.Signature);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("The message signature is malformed.", exception);
        }

        var signaturePayload = ChatMessageAuthenticator.CreateSignaturePayload(message);

        try
        {
            if (!sender.VerifySignature(signaturePayload, signature))
            {
                throw new CryptographicException("The message signature is invalid.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signaturePayload);
        }
    }

    private static void ValidateConversationMetadata(
        ChatMessage message,
        IdentityKeyPair recipient)
    {
        if (string.IsNullOrWhiteSpace(message.ConversationName)
            || message.ConversationName.Length
                > ChatMessage.MaximumConversationNameLength)
        {
            throw new CryptographicException(
                "The signed conversation name is invalid.");
        }

        if (message.ParticipantFingerprints.Count is < 2
            or > ChatMessage.MaximumParticipantCount)
        {
            throw new CryptographicException(
                "The signed participant list has an invalid size.");
        }

        Fingerprint[] participants;
        Fingerprint[] recipients;
        Fingerprint senderFingerprint;

        try
        {
            senderFingerprint = new Fingerprint(message.SenderFingerprint);
            participants = message.ParticipantFingerprints
                .Select(static value => new Fingerprint(value))
                .ToArray();
            recipients = message.RecipientKeys
                .Select(
                    static recipient =>
                        new Fingerprint(recipient.RecipientFingerprint))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException)
        {
            throw new CryptographicException(
                "The signed conversation contains a malformed fingerprint.",
                exception);
        }

        if (participants.Distinct().Count() != participants.Length
            || recipients.Distinct().Count() != recipients.Length
            || !participants.Contains(senderFingerprint))
        {
            throw new CryptographicException(
                "The signed conversation participant list is inconsistent.");
        }

        var expectedParticipants = recipients
            .Append(senderFingerprint)
            .OrderBy(static fingerprint => fingerprint.Value, StringComparer.Ordinal)
            .ToArray();
        var normalizedParticipants = participants
            .OrderBy(static fingerprint => fingerprint.Value, StringComparer.Ordinal)
            .ToArray();

        if (!normalizedParticipants.SequenceEqual(expectedParticipants)
            || !normalizedParticipants.Contains(recipient.Fingerprint))
        {
            throw new CryptographicException(
                "The signed conversation participant list does not match its recipient keys.");
        }
    }
}
