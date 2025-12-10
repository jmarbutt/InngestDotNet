namespace Inngest.Steps;

/// <summary>
/// Step tools interface providing step primitives for building durable Inngest functions.
///
/// Steps are the fundamental building blocks of Inngest functions. Each step:
/// - Is automatically retried on failure
/// - Has its result memoized to prevent re-execution
/// - Can survive function restarts and serverless cold starts
/// </summary>
public interface IStepTools
{
    /// <summary>
    /// Execute async code with memoization and automatic retries.
    /// If this step has already been executed, returns the cached result.
    /// </summary>
    /// <typeparam name="T">The return type of the step</typeparam>
    /// <param name="id">Unique identifier for the step (used for memoization)</param>
    /// <param name="handler">The async function to execute</param>
    /// <param name="options">Optional step configuration</param>
    /// <returns>The result of the step execution</returns>
    Task<T> Run<T>(string id, Func<Task<T>> handler, StepRunOptions? options = null);

    /// <summary>
    /// Execute synchronous code with memoization and automatic retries.
    /// </summary>
    /// <typeparam name="T">The return type of the step</typeparam>
    /// <param name="id">Unique identifier for the step</param>
    /// <param name="handler">The function to execute</param>
    /// <param name="options">Optional step configuration</param>
    /// <returns>The result of the step execution</returns>
    Task<T> Run<T>(string id, Func<T> handler, StepRunOptions? options = null);

    /// <summary>
    /// Execute async code that returns void (represented as bool for memoization).
    /// </summary>
    /// <param name="id">Unique identifier for the step</param>
    /// <param name="handler">The async action to execute</param>
    /// <param name="options">Optional step configuration</param>
    Task Run(string id, Func<Task> handler, StepRunOptions? options = null);

    /// <summary>
    /// Sleep for a duration. The function execution will pause and resume after the duration.
    /// This is a durable sleep - it survives function restarts and doesn't consume compute resources.
    /// </summary>
    /// <param name="id">Unique identifier for the sleep step</param>
    /// <param name="duration">Duration string (e.g., "5m", "1h", "7d") or ISO 8601 duration</param>
    Task Sleep(string id, string duration);

    /// <summary>
    /// Sleep for a TimeSpan duration.
    /// </summary>
    /// <param name="id">Unique identifier for the sleep step</param>
    /// <param name="duration">The duration to sleep</param>
    Task Sleep(string id, TimeSpan duration);

    /// <summary>
    /// Sleep until a specific point in time.
    /// </summary>
    /// <param name="id">Unique identifier for the sleep step</param>
    /// <param name="until">The datetime to sleep until (will be converted to UTC)</param>
    Task SleepUntil(string id, DateTimeOffset until);

    /// <summary>
    /// Wait for a matching event to arrive. Returns null if the timeout is reached.
    /// </summary>
    /// <typeparam name="T">The expected event data type</typeparam>
    /// <param name="id">Unique identifier for the wait step</param>
    /// <param name="options">Configuration for the event to wait for</param>
    /// <returns>The matching event, or null if timeout was reached</returns>
    Task<T?> WaitForEvent<T>(string id, WaitForEventOptions options) where T : class;

    /// <summary>
    /// Invoke another Inngest function and wait for its result.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="id">Unique identifier for the invoke step</param>
    /// <param name="options">Configuration for the function to invoke</param>
    /// <returns>The result from the invoked function, or null if it returns void</returns>
    Task<T?> Invoke<T>(string id, InvokeOptions options) where T : class;

    /// <summary>
    /// Send one or more events to Inngest from within a function.
    /// This is a memoized step - the events will only be sent once.
    /// </summary>
    /// <param name="id">Unique identifier for the send step</param>
    /// <param name="events">The events to send</param>
    /// <returns>Array of event IDs that were created</returns>
    Task<string[]> SendEvent(string id, params InngestEvent[] events);
}

/// <summary>
/// Options for configuring a step.Run execution
/// </summary>
public class StepRunOptions
{
    /// <summary>
    /// Human-readable name for the step (shown in Inngest dashboard)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Retry configuration for this specific step
    /// </summary>
    public RetryOptions? Retry { get; set; }
}

/// <summary>
/// Options for configuring a WaitForEvent step
/// </summary>
public class WaitForEventOptions
{
    /// <summary>
    /// The event name to wait for (e.g., "stripe/payment.succeeded")
    /// </summary>
    public required string Event { get; set; }

    /// <summary>
    /// Timeout duration after which the step returns null.
    /// Format: "5m", "1h", "7d", etc.
    /// </summary>
    public required string Timeout { get; set; }

    /// <summary>
    /// Optional CEL expression to match specific events.
    /// Example: "async.data.userId == event.data.userId"
    /// The 'event' refers to the triggering event, 'async' refers to the incoming event.
    /// </summary>
    public string? Match { get; set; }

    /// <summary>
    /// Optional: If specified, only wait for events that match this expression
    /// </summary>
    public string? If { get; set; }
}

/// <summary>
/// Options for configuring an Invoke step
/// </summary>
public class InvokeOptions
{
    /// <summary>
    /// The function ID to invoke. Can be just the function ID (e.g., "send-email")
    /// or the full composite ID (e.g., "my-app-send-email").
    /// </summary>
    public required string FunctionId { get; set; }

    /// <summary>
    /// Data to pass to the invoked function (will be available as event.data)
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Timeout for the invocation. If the invoked function doesn't complete
    /// within this time, the step will fail.
    /// </summary>
    public string? Timeout { get; set; }

    /// <summary>
    /// Optional user data to include in the event
    /// </summary>
    public object? User { get; set; }
}
