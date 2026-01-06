namespace Inngest;

/// <summary>
/// Interface for Inngest event according to the SDK specification
/// </summary>
public interface IInngestEvent
{
    /// <summary>
    /// Unique identifier for the event
    /// </summary>
    public string? Id { get; set; }
    
    /// <summary>
    /// Name of the event in dot.separated.format
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Custom data payload for the event
    /// </summary>
    public object? Data { get; set; }
    
    /// <summary>
    /// Timestamp in Unix milliseconds when the event occurred
    /// </summary>
    public long Timestamp { get; set; }
    
    /// <summary>
    /// Optional user information associated with the event
    /// </summary>
    public object? User { get; set; }
}
