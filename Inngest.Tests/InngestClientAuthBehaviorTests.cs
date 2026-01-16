using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Inngest.Tests;

public class InngestClientAuthBehaviorTests
{
    [Fact]
    public async Task HandleRequestAsync_WhenInngestDevEnvFalse_DoesNotRequireSignatureForPut()
    {
        // PUT (sync) requests should NOT require signature verification per SDK spec
        // This allows manual curl testing of sync endpoints
        var previous = Environment.GetEnvironmentVariable("INNGEST_DEV");
        try
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", "false");

            var client = new InngestClient(eventKey: "evt", signingKey: "signkey-test-abc123");

            var context = new DefaultHttpContext();
            context.Request.Method = "PUT";
            context.Request.Headers.Host = "localhost:5000";
            context.Request.Scheme = "http";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));
            context.Request.ContentLength = context.Request.Body.Length;

            await client.HandleRequestAsync(context);

            // Should NOT return 401 - PUT doesn't require signatures
            Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", previous);
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenInngestDevEnvTrue_SkipsSignatureForPut()
    {
        var previous = Environment.GetEnvironmentVariable("INNGEST_DEV");
        try
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", "true");

            var client = new InngestClient(eventKey: "evt", signingKey: "signkey-test-abc123");

            var context = new DefaultHttpContext();
            context.Request.Method = "PUT";
            context.Request.Headers.Host = "localhost:5000";
            context.Request.Scheme = "http";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));
            context.Request.ContentLength = context.Request.Body.Length;

            await client.HandleRequestAsync(context);

            Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", previous);
        }
    }

    [Fact]
    public async Task HandleRequestAsync_WhenTimestampIsTooFarInFuture_RejectsPost()
    {
        // Signature timestamp validation only applies to POST requests
        // since PUT/GET don't require signatures
        var previous = Environment.GetEnvironmentVariable("INNGEST_DEV");
        try
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", null);

            const string signingKey = "signkey-test-abc123";
            var client = new InngestClient(eventKey: "evt", signingKey: signingKey);

            var body = "{\"event\":{\"name\":\"test\",\"data\":{}}}";
            var timestamp = DateTimeOffset.UtcNow.AddMinutes(6).ToUnixTimeSeconds();
            var signature = ComputeSignature(body, timestamp, signingKey);

            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Headers.Host = "localhost:5000";
            context.Request.Scheme = "http";
            context.Request.Headers["X-Inngest-Signature"] = $"t={timestamp}&s={signature}";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = context.Request.Body.Length;

            await client.HandleRequestAsync(context);

            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", previous);
        }
    }

    private static string ComputeSignature(string body, long timestamp, string signingKey)
    {
        var normalizedKey = NormalizeSigningKey(signingKey);
        var dataToSign = body + timestamp;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(normalizedKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private static string NormalizeSigningKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;

        // Matches SDK regex: ^signkey-\\w+-
        var idx = key.IndexOf('-', "signkey-".Length);
        if (!key.StartsWith("signkey-", StringComparison.Ordinal) || idx < 0)
            return key;

        return key[(idx + 1)..];
    }
}

