namespace Inngest;

using System.Text.Json.Serialization;

/// <summary>
/// The payload received from Inngest during a function call
/// </summary>
public class CallRequestPayload
{
    /// <summary>
    /// The primary event that triggered the function
    /// </summary>
    [JsonPropertyName("event")]
    public InngestEvent Event { get; set; } = new();
    
    /// <summary>
    /// All events involved in this function execution
    /// </summary>
    [JsonPropertyName("events")]
    public IEnumerable<InngestEvent> Events { get; set; } = Array.Empty<InngestEvent>();
    
    /// <summary>
    /// Step execution results stored from previous runs
    /// </summary>
    [JsonPropertyName("steps")]
    public Dictionary<string, object> Steps { get; set; } = new();
    
    /// <summary>
    /// Context information about the function execution
    /// </summary>
    [JsonPropertyName("ctx")]
    public CallRequestContext Ctx { get; set; } = new();
    
    /// <summary>
    /// Secrets available to the function
    /// </summary>
    [JsonPropertyName("secrets")]
    public Dictionary<string, string>? Secrets { get; set; }
}