namespace Inngest;

using System.Text.Json.Serialization;

/// <summary>
/// Context information for a function execution
/// </summary>
public class CallRequestContext
{
    /// <summary>
    /// Unique identifier for this function run
    /// </summary>
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current retry attempt number
    /// </summary>
    [JsonPropertyName("attempt")]
    public int Attempt { get; set; }
    
    /// <summary>
    /// Whether immediate execution is disabled
    /// </summary>
    [JsonPropertyName("disable_immediate_execution")]
    public bool DisableImmediateExecution { get; set; }
    
    /// <summary>
    /// Whether to use the API for execution
    /// </summary>
    [JsonPropertyName("use_api")]
    public bool UseApi { get; set; }
    
    /// <summary>
    /// The ID of the function to execute
    /// </summary>
    [JsonPropertyName("fn_id")]
    public string FunctionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Environment variables for the function
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}