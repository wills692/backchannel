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
        ArgumentNullException.ThrowIfNull(plaintext);
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
                SenderFingerprint = sender.Fingerprint.Value,
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
}
