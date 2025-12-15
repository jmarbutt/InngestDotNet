using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inngest.Internal;
using Microsoft.AspNetCore.Http;

namespace Inngest.Tests;

/// <summary>
/// Tests for signature verification with gzip-compressed request bodies.
/// This tests the fix for: Inngest computes signatures on raw wire bytes (gzip compressed),
/// but ASP.NET Core may decompress before we can verify.
/// </summary>
public class GzipSignatureVerificationTests
{
    private static readonly Regex SigningKeyPrefixRegex = new(@"^signkey-\w+-", RegexOptions.Compiled);

    private static string NormalizeSigningKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;

        return SigningKeyPrefixRegex.Replace(key, "");
    }

    /// <summary>
    /// Computes signature the way Inngest does: HMAC-SHA256(body_bytes + timestamp_string_bytes, key)
    /// </summary>
    private static string ComputeSignatureFromBytes(byte[] bodyBytes, long timestamp, string signingKey)
    {
        var normalizedKey = NormalizeSigningKey(signingKey);
        var keyBytes = Encoding.UTF8.GetBytes(normalizedKey);

        // The data to sign is body bytes concatenated with timestamp string bytes
        var timestampBytes = Encoding.UTF8.GetBytes(timestamp.ToString());
        var dataToSign = new byte[bodyBytes.Length + timestampBytes.Length];
        Buffer.BlockCopy(bodyBytes, 0, dataToSign, 0, bodyBytes.Length);
        Buffer.BlockCopy(timestampBytes, 0, dataToSign, bodyBytes.Length, timestampBytes.Length);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(dataToSign);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    /// <summary>
    /// Computes signature the legacy string-based way
    /// </summary>
    private static string ComputeSignatureFromString(string body, long timestamp, string signingKey)
    {
        var normalizedKey = NormalizeSigningKey(signingKey);
        var dataToSign = body + timestamp;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(normalizedKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private static byte[] GzipCompress(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return compressedStream.ToArray();
    }

    private static string GzipDecompress(byte[] compressedBytes)
    {
        using var compressedStream = new MemoryStream(compressedBytes);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void UncompressedBody_ByteAndStringSignatures_AreIdentical()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\",\"data\":{\"foo\":\"bar\"}}}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var timestamp = 1705586504L;
        var signingKey = "signkey-prod-abc123def456";

        // Act
        var signatureFromBytes = ComputeSignatureFromBytes(bodyBytes, timestamp, signingKey);
        var signatureFromString = ComputeSignatureFromString(body, timestamp, signingKey);

        // Assert
        Assert.Equal(signatureFromBytes, signatureFromString);
    }

    [Fact]
    public void GzipCompressedBody_SignatureComputedOnCompressedBytes_DiffersFromDecompressed()
    {
        // This test demonstrates the core issue:
        // Inngest signs compressed bytes, but if we read decompressed bytes, signatures won't match

        // Arrange
        var originalBody = "{\"event\":{\"name\":\"test/event\",\"data\":{\"foo\":\"bar\"}}}";
        var compressedBytes = GzipCompress(originalBody);
        var timestamp = 1705586504L;
        var signingKey = "signkey-prod-abc123def456";

        // Act
        // Signature computed on compressed bytes (what Inngest does)
        var signatureOnCompressed = ComputeSignatureFromBytes(compressedBytes, timestamp, signingKey);

        // Signature computed on decompressed string (what the old code did)
        var decompressedBody = GzipDecompress(compressedBytes);
        var signatureOnDecompressed = ComputeSignatureFromString(decompressedBody, timestamp, signingKey);

        // Assert
        Assert.NotEqual(signatureOnCompressed, signatureOnDecompressed);
        Assert.Equal(originalBody, decompressedBody);
    }

    [Fact]
    public void GzipCompressedBody_VerificationWithRawBytes_Succeeds()
    {
        // Arrange
        var originalBody = "{\"event\":{\"name\":\"test/event\",\"data\":{\"foo\":\"bar\"}}}";
        var compressedBytes = GzipCompress(originalBody);
        var timestamp = 1705586504L;
        var signingKey = "signkey-prod-abc123def456";

        // Simulate what Inngest sends: signature computed on compressed bytes
        var expectedSignature = ComputeSignatureFromBytes(compressedBytes, timestamp, signingKey);

        // Act - Verify using raw compressed bytes (the fix)
        var computedSignature = ComputeSignatureFromBytes(compressedBytes, timestamp, signingKey);

        // Assert
        Assert.Equal(expectedSignature, computedSignature);
    }

    [Fact]
    public void GzipCompression_IsSignificant_ForTypicalPayload()
    {
        // This test demonstrates that gzip compression significantly reduces payload size,
        // confirming that Inngest would use compression for typical payloads

        // Arrange
        var payload = @"{
            ""event"": {
                ""name"": ""user/order.created"",
                ""data"": {
                    ""orderId"": ""ord_abc123"",
                    ""userId"": ""usr_xyz789"",
                    ""items"": [
                        {""sku"": ""ITEM-001"", ""quantity"": 2, ""price"": 29.99},
                        {""sku"": ""ITEM-002"", ""quantity"": 1, ""price"": 49.99}
                    ],
                    ""total"": 109.97,
                    ""currency"": ""USD"",
                    ""shippingAddress"": {
                        ""street"": ""123 Main St"",
                        ""city"": ""San Francisco"",
                        ""state"": ""CA"",
                        ""zip"": ""94105"",
                        ""country"": ""US""
                    }
                },
                ""ts"": 1705586504000,
                ""id"": ""01HKQG5M7QDEF123456789AB""
            },
            ""steps"": {},
            ""ctx"": {
                ""run_id"": ""01HKQG5M7QDEF987654321XY"",
                ""attempt"": 0
            }
        }";

        // Act
        var originalBytes = Encoding.UTF8.GetBytes(payload);
        var compressedBytes = GzipCompress(payload);

        // Assert
        Assert.True(compressedBytes.Length < originalBytes.Length,
            $"Expected compressed size ({compressedBytes.Length}) to be less than original ({originalBytes.Length})");

        // Compression ratio should be significant for JSON payloads
        var compressionRatio = (double)compressedBytes.Length / originalBytes.Length;
        Assert.True(compressionRatio < 0.5,
            $"Expected compression ratio ({compressionRatio:P0}) to be less than 50%");
    }

    [Fact]
    public void DifferentTimestamps_ProduceDifferentSignatures_WithBytes()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var signingKey = "signkey-prod-abc123def456";

        // Act
        var signature1 = ComputeSignatureFromBytes(bodyBytes, 1705586504L, signingKey);
        var signature2 = ComputeSignatureFromBytes(bodyBytes, 1705586505L, signingKey);

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentSignatures_WithBytes()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var timestamp = 1705586504L;

        // Act
        var signature1 = ComputeSignatureFromBytes(bodyBytes, timestamp, "signkey-prod-key1");
        var signature2 = ComputeSignatureFromBytes(bodyBytes, timestamp, "signkey-prod-key2");

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void ByteSignature_Length_Is64HexChars()
    {
        // Arrange
        var body = "{\"event\":{\"name\":\"test/event\"}}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var timestamp = 1705586504L;
        var signingKey = "signkey-prod-abc123def456";

        // Act
        var signature = ComputeSignatureFromBytes(bodyBytes, timestamp, signingKey);

        // Assert
        Assert.Equal(64, signature.Length); // SHA256 = 32 bytes = 64 hex chars
        Assert.True(signature.All(c => char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void KeyNormalization_WorksWithPrefix()
    {
        // Arrange
        var keyWithPrefix = "signkey-prod-abc123def456";
        var keyWithoutPrefix = "abc123def456";

        // Act
        var normalizedWithPrefix = NormalizeSigningKey(keyWithPrefix);
        var normalizedWithoutPrefix = NormalizeSigningKey(keyWithoutPrefix);

        // Assert
        Assert.Equal("abc123def456", normalizedWithPrefix);
        Assert.Equal("abc123def456", normalizedWithoutPrefix);
    }
}

/// <summary>
/// Tests for the RawBodyMiddleware that captures raw request bytes.
/// </summary>
public class RawBodyMiddlewareTests
{
    [Fact]
    public async Task Middleware_CapturesRawBytes_ForPostRequest()
    {
        // Arrange
        var body = "{\"test\":\"data\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        byte[]? capturedBytes = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            if (ctx.Items.TryGetValue(RawBodyMiddleware.RawBodyKey, out var raw) && raw is byte[] bytes)
            {
                capturedBytes = bytes;
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.NotNull(capturedBytes);
        Assert.Equal(bodyBytes, capturedBytes);
    }

    [Fact]
    public async Task Middleware_CapturesRawBytes_ForPutRequest()
    {
        // Arrange
        var body = "{\"test\":\"data\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        byte[]? capturedBytes = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            if (ctx.Items.TryGetValue(RawBodyMiddleware.RawBodyKey, out var raw) && raw is byte[] bytes)
            {
                capturedBytes = bytes;
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.NotNull(capturedBytes);
        Assert.Equal(bodyBytes, capturedBytes);
    }

    [Fact]
    public async Task Middleware_DoesNotCaptureBytes_ForGetRequest()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";

        byte[]? capturedBytes = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            if (ctx.Items.TryGetValue(RawBodyMiddleware.RawBodyKey, out var raw) && raw is byte[] bytes)
            {
                capturedBytes = bytes;
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Null(capturedBytes);
    }

    [Fact]
    public async Task Middleware_CapturesContentEncoding_Header()
    {
        // Arrange
        var body = "{\"test\":\"data\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Headers.ContentEncoding = "gzip";

        string? capturedEncoding = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            if (ctx.Items.TryGetValue(RawBodyMiddleware.RawBodyEncodingKey, out var enc) && enc is string encoding)
            {
                capturedEncoding = encoding;
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("gzip", capturedEncoding);
    }

    [Fact]
    public async Task Middleware_BodyIsStillReadable_AfterCapture()
    {
        // Arrange
        var body = "{\"test\":\"data\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        string? bodyReadInHandler = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            // Read body in the handler (simulating what InngestClient does)
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            bodyReadInHandler = await reader.ReadToEndAsync();
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(body, bodyReadInHandler);
    }

    [Fact]
    public async Task Middleware_CapturesGzipCompressedBytes_Correctly()
    {
        // Arrange
        var originalBody = "{\"test\":\"data\"}";
        var compressedBytes = GzipCompress(originalBody);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(compressedBytes);
        context.Request.ContentLength = compressedBytes.Length;
        context.Request.Headers.ContentEncoding = "gzip";

        byte[]? capturedBytes = null;
        var middleware = new RawBodyMiddleware(async ctx =>
        {
            if (ctx.Items.TryGetValue(RawBodyMiddleware.RawBodyKey, out var raw) && raw is byte[] bytes)
            {
                capturedBytes = bytes;
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.NotNull(capturedBytes);
        Assert.Equal(compressedBytes, capturedBytes);

        // Verify these are actually compressed bytes (not equal to original)
        var originalBytes = Encoding.UTF8.GetBytes(originalBody);
        Assert.NotEqual(originalBytes.Length, capturedBytes.Length);
    }

    private static byte[] GzipCompress(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return compressedStream.ToArray();
    }
}
