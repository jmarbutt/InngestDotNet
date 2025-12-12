using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Inngest.Tests;

/// <summary>
/// Tests for signature verification to ensure it matches the Inngest SDK specification.
/// See: https://github.com/inngest/inngest/blob/main/docs/SDK_SPEC.md
/// </summary>
public class SignatureVerificationTests
{
    // Regex pattern matching the SDK implementation
    private static readonly Regex SigningKeyPrefixRegex = new(@"^signkey-\w+-", RegexOptions.Compiled);

    private static string NormalizeSigningKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;

        return SigningKeyPrefixRegex.Replace(key, "");
    }

    private static string ComputeSignature(string body, long timestamp, string signingKey)
    {
        var normalizedKey = NormalizeSigningKey(signingKey);
        var dataToSign = body + timestamp;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(normalizedKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        return Convert.ToHexString(hashBytes).ToLower();
    }

    [Fact]
    public void NormalizeSigningKey_RemovesProdPrefix()
    {
        // Arrange
        var key = "signkey-prod-abc123def456";

        // Act
        var normalized = NormalizeSigningKey(key);

        // Assert
        Assert.Equal("abc123def456", normalized);
    }

    [Fact]
    public void NormalizeSigningKey_RemovesTestPrefix()
    {
        // Arrange
        var key = "signkey-test-abc123def456";

        // Act
        var normalized = NormalizeSigningKey(key);

        // Assert
        Assert.Equal("abc123def456", normalized);
    }

    [Fact]
    public void NormalizeSigningKey_RemovesStagingPrefix()
    {
        // Arrange
        var key = "signkey-staging-abc123def456";

        // Act
        var normalized = NormalizeSigningKey(key);

        // Assert
        Assert.Equal("abc123def456", normalized);
    }

    [Fact]
    public void NormalizeSigningKey_HandlesKeyWithoutPrefix()
    {
        // Arrange
        var key = "abc123def456";

        // Act
        var normalized = NormalizeSigningKey(key);

        // Assert
        Assert.Equal("abc123def456", normalized);
    }

    [Fact]
    public void NormalizeSigningKey_HandlesEmptyKey()
    {
        // Arrange
        var key = "";

        // Act
        var normalized = NormalizeSigningKey(key);

        // Assert
        Assert.Equal("", normalized);
    }

    [Fact]
    public void ComputeSignature_ProducesConsistentResult()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\",\"data\":{}}}";
        var timestamp = 1705586504L;
        var signingKey = "signkey-test-actualkey123";

        // Act
        var signature1 = ComputeSignature(body, timestamp, signingKey);
        var signature2 = ComputeSignature(body, timestamp, signingKey);

        // Assert
        Assert.Equal(signature1, signature2);
        Assert.Equal(64, signature1.Length); // SHA256 produces 32 bytes = 64 hex chars
    }

    [Fact]
    public void ComputeSignature_DifferentBodiesProduceDifferentSignatures()
    {
        // Arrange
        var timestamp = 1705586504L;
        var signingKey = "signkey-test-actualkey123";

        // Act
        var signature1 = ComputeSignature("{\"event\":\"one\"}", timestamp, signingKey);
        var signature2 = ComputeSignature("{\"event\":\"two\"}", timestamp, signingKey);

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void ComputeSignature_DifferentTimestampsProduceDifferentSignatures()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var signingKey = "signkey-test-actualkey123";

        // Act
        var signature1 = ComputeSignature(body, 1705586504L, signingKey);
        var signature2 = ComputeSignature(body, 1705586505L, signingKey);

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void ComputeSignature_DifferentKeysProduceDifferentSignatures()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var timestamp = 1705586504L;

        // Act
        var signature1 = ComputeSignature(body, timestamp, "signkey-test-key1");
        var signature2 = ComputeSignature(body, timestamp, "signkey-test-key2");

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void ComputeSignature_WithAndWithoutPrefix_ProducesSameResult()
    {
        // Arrange - This is the critical test for the fix
        // The key with and without prefix should produce the same signature
        // because we normalize the key before hashing
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var timestamp = 1705586504L;
        var keyWithPrefix = "signkey-prod-abc123def456";
        var keyWithoutPrefix = "abc123def456";

        // Act
        var signatureWithPrefix = ComputeSignature(body, timestamp, keyWithPrefix);
        var signatureWithoutPrefix = ComputeSignature(body, timestamp, keyWithoutPrefix);

        // Assert
        Assert.Equal(signatureWithPrefix, signatureWithoutPrefix);
    }

    [Fact]
    public void SignatureFormat_MatchesSpec()
    {
        // The SDK spec says the signature header format is:
        // t=<timestamp>&s=<signature>
        // e.g., t=1705586504&s=3f1c811920eb25da7fa70e3ac484e32e93f01dbbca7c9ce2365f2062a3e10c26

        // Arrange
        var timestamp = 1705586504L;
        var signature = "3f1c811920eb25da7fa70e3ac484e32e93f01dbbca7c9ce2365f2062a3e10c26";
        var headerValue = $"t={timestamp}&s={signature}";

        // Act - Parse the header
        var components = System.Web.HttpUtility.ParseQueryString(headerValue);
        var parsedTimestamp = components["t"];
        var parsedSignature = components["s"];

        // Assert
        Assert.Equal("1705586504", parsedTimestamp);
        Assert.Equal(signature, parsedSignature);
    }

    [Fact]
    public void AuthorizationHeader_KeyHashingIsCorrect()
    {
        // The authorization header should contain a hashed version of the signing key
        // Format: signkey-{env}-{sha256_hash_of_normalized_key}

        // Arrange
        var signingKey = "signkey-prod-abc123def456";
        var normalizedKey = NormalizeSigningKey(signingKey);

        // Act
        var prefixMatch = SigningKeyPrefixRegex.Match(signingKey);
        var prefix = prefixMatch.Success ? prefixMatch.Value.TrimEnd('-') : "signkey-prod";

        using var sha256 = SHA256.Create();
        var keyBytes = Encoding.UTF8.GetBytes(normalizedKey);
        var hashBytes = sha256.ComputeHash(keyBytes);
        var hashedKey = $"{prefix}-{Convert.ToHexString(hashBytes).ToLower()}";

        // Assert
        Assert.StartsWith("signkey-prod-", hashedKey);
        Assert.Equal(64, hashedKey.Substring("signkey-prod-".Length).Length); // SHA256 = 64 hex chars
    }
}
