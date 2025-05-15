namespace Inngest;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Represents an event in the Inngest system
/// </summary>
public class InngestEvent
{
    /// <summary>
    /// Unique identifier for the event
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Name of the event in dot.separated.format
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Custom data payload for the event
    /// </summary>
    [JsonPropertyName("data")]
    public object Data { get; set; } = new();

    /// <summary>
    /// Timestamp in Unix milliseconds when the event occurred
    /// </summary>
    [JsonPropertyName("ts")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Optional user information associated with the event
    /// </summary>
    [JsonPropertyName("user")]
    public object? User { get; set; }

    /// <summary>
    /// Optional version of the event schema
    /// </summary>
    [JsonPropertyName("v")]
    public string? Version { get; set; }

    /// <summary>
    /// Optional errors associated with this event
    /// </summary>
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    /// <summary>
    /// Optional idempotency key to prevent duplicate processing
    /// </summary>
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Creates a new empty InngestEvent
    /// </summary>
    public InngestEvent() { }

    /// <summary>
    /// Creates a new InngestEvent with the specified name and data
    /// </summary>
    /// <param name="name">The name of the event</param>
    /// <param name="data">The data payload for the event</param>
    public InngestEvent(string name, object data)
    {
        Name = name;
        Data = data;
        Id = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Adds user information to the event
    /// </summary>
    /// <param name="userInfo">User information to associate with the event</param>
    /// <returns>The event instance for method chaining</returns>
    public InngestEvent WithUser(object userInfo)
    {
        User = userInfo;
        return this;
    }

    /// <summary>
    /// Sets a custom idempotency key to prevent duplicate processing
    /// </summary>
    /// <param name="key">The idempotency key</param>
    /// <returns>The event instance for method chaining</returns>
    public InngestEvent WithIdempotencyKey(string key)
    {
        IdempotencyKey = key;
        return this;
    }
}