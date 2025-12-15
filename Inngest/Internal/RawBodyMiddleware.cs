using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Inngest.Internal;

/// <summary>
/// Middleware that captures the raw request body bytes before any decompression or processing.
/// This is critical for signature verification, as Inngest computes signatures on the raw wire bytes,
/// which may be gzip-compressed. If any middleware (like UseRequestDecompression) or proxy
/// decompresses the body, we need the original bytes to verify the signature.
/// </summary>
public class RawBodyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RawBodyMiddleware>? _logger;

    /// <summary>
    /// Key used to store raw body bytes in HttpContext.Items
    /// </summary>
    public const string RawBodyKey = "Inngest.RawBody";

    /// <summary>
    /// Key used to store raw body content encoding in HttpContext.Items
    /// </summary>
    public const string RawBodyEncodingKey = "Inngest.RawBodyEncoding";

    /// <summary>
    /// Creates a new instance of the raw body capture middleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public RawBodyMiddleware(RequestDelegate next, ILogger<RawBodyMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware to capture raw body bytes for POST/PUT requests.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Only capture body for POST and PUT requests (the ones Inngest uses)
        if (context.Request.Method == "POST" || context.Request.Method == "PUT")
        {
            await CaptureRawBodyAsync(context);
        }

        await _next(context);
    }

    private async Task CaptureRawBodyAsync(HttpContext context)
    {
        var request = context.Request;

        // Enable buffering to allow multiple reads
        request.EnableBuffering();

        // Store the Content-Encoding header value (if any) before any middleware removes it
        var contentEncoding = request.Headers.ContentEncoding.ToString();
        if (!string.IsNullOrEmpty(contentEncoding))
        {
            context.Items[RawBodyEncodingKey] = contentEncoding;
            _logger?.LogDebug("Captured Content-Encoding header: {Encoding}", contentEncoding);
        }

        // Read raw bytes from the body stream
        // This captures bytes as they came over the wire (potentially compressed)
        using var memoryStream = new MemoryStream();
        await request.Body.CopyToAsync(memoryStream);

        var rawBytes = memoryStream.ToArray();
        context.Items[RawBodyKey] = rawBytes;

        _logger?.LogDebug(
            "Captured raw body: {Length} bytes, Content-Encoding: {Encoding}, Content-Length header: {ContentLength}",
            rawBytes.Length,
            contentEncoding,
            request.ContentLength);

        // Reset the position so downstream can read again
        request.Body.Position = 0;
    }
}
