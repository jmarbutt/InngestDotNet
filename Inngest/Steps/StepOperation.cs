using System.Text.Json.Serialization;

namespace Inngest.Steps;

/// <summary>
/// Opcodes that tell Inngest what type of step operation to schedule
/// </summary>
public static class StepOpCode
{
    /// <summary>Run synchronous code with memoization</summary>
    public const string StepRun = "StepRun";

    /// <summary>Step completed successfully (used in response)</summary>
    public const string StepPlanned = "StepPlanned";

    /// <summary>Step encountered an error</summary>
    public const string StepError = "StepError";

    /// <summary>Sleep for a duration or until a specific time</summary>
    public const string Sleep = "Sleep";

    /// <summary>Wait for a matching event to arrive</summary>
    public const string WaitForEvent = "WaitForEvent";

    /// <summary>Invoke another Inngest function</summary>
    public const string InvokeFunction = "InvokeFunction";

    /// <summary>Send one or more events</summary>
    public const string Step = "Step";
}

/// <summary>
/// Represents an operation (opcode) that tells Inngest what to schedule.
/// This is the core communication protocol between the SDK and Inngest server.
/// </summary>
public class StepOperation
{
    /// <summary>
    /// Unique identifier for this step (used for memoization)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The operation type (see <see cref="StepOpCode"/>)
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    /// <summary>
    /// Operation-specific options (e.g., sleep duration, event to wait for)
    /// </summary>
    [JsonPropertyName("opts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Opts { get; set; }

    /// <summary>
    /// The result data from executing the step
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    /// <summary>
    /// Human-readable name for the step (shown in Inngest dashboard)
    /// </summary>
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Error information if the step failed
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StepError? Error { get; set; }
}

/// <summary>
/// Error information for a failed step
/// </summary>
public class StepError
{
    /// <summary>
    /// The exception type name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The error message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The stack trace (optional)
    /// </summary>
    [JsonPropertyName("stack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stack { get; set; }

    /// <summary>
    /// Create a StepError from an exception
    /// </summary>
    public static StepError FromException(Exception ex) => new()
    {
        Name = ex.GetType().Name,
        Message = ex.Message,
        Stack = ex.StackTrace
    };
}

/// <summary>
/// Options for Sleep operations
/// </summary>
public class SleepOpts
{
    /// <summary>
    /// Duration to sleep. Can be ISO 8601 duration or human-readable format (e.g., "5m", "1h", "7d")
    /// or an ISO 8601 datetime for sleeping until a specific time.
    /// </summary>
    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;
}

/// <summary>
/// Options for WaitForEvent operations
/// </summary>
public class WaitForEventOpts
{
    /// <summary>
    /// The event name to wait for
    /// </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Timeout duration (e.g., "1h", "7d"). After this, the step returns null.
    /// </summary>
    [JsonPropertyName("timeout")]
    public string Timeout { get; set; } = string.Empty;

    /// <summary>
    /// Optional CEL expression to match events (e.g., "async.data.userId == event.data.userId")
    /// </summary>
    [JsonPropertyName("if")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? If { get; set; }
}

/// <summary>
/// Options for InvokeFunction operations
/// </summary>
public class InvokeFunctionOpts
{
    /// <summary>
    /// The function ID to invoke (e.g., "app-id-function-id" or just "function-id")
    /// </summary>
    [JsonPropertyName("function_id")]
    public string FunctionId { get; set; } = string.Empty;

    /// <summary>
    /// The event payload to send to the invoked function
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    /// <summary>
    /// Optional timeout for the invocation (e.g., "1h")
    /// </summary>
    [JsonPropertyName("timeout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Timeout { get; set; }
}

/// <summary>
/// Options for SendEvent step operations
/// </summary>
public class SendEventOpts
{
    /// <summary>
    /// The step type identifier for sendEvent operations
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "step.sendEvent";

    /// <summary>
    /// The events to send
    /// </summary>
    [JsonPropertyName("ops")]
    public object[]? Ops { get; set; }
}
