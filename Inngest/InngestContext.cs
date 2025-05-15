using System.Text.Json;

namespace Inngest;

/// <summary>
/// Execution context for Inngest functions providing access to event data, steps, and secrets
/// </summary>
public class InngestContext
{
    /// <summary>
    /// The event that triggered this function execution
    /// </summary>
    public InngestEvent Event { get; }
    
    /// <summary>
    /// All events that were part of this function execution request
    /// </summary>
    public IEnumerable<InngestEvent> Events { get; }
    
    /// <summary>
    /// Dictionary of completed step results that can be used for step memoization
    /// </summary>
    public Dictionary<string, object> Steps { get; }
    
    /// <summary>
    /// Context information about the current function execution
    /// </summary>
    public CallRequestContext Ctx { get; }
    
    private readonly Dictionary<string, string> _secrets;
    private readonly IInngestClient? _client;

    /// <summary>
    /// Creates a new InngestContext with the specified parameters
    /// </summary>
    /// <param name="evt">The event that triggered the function</param>
    /// <param name="events">All events in the request</param>
    /// <param name="steps">Dictionary of completed step results</param>
    /// <param name="ctx">Request context information</param>
    /// <param name="secrets">Secret values accessible to the function</param>
    /// <param name="client">Reference to the InngestClient for sending events</param>
    public InngestContext(
        InngestEvent evt,
        IEnumerable<InngestEvent> events,
        Dictionary<string, object> steps,
        CallRequestContext ctx,
        Dictionary<string, string>? secrets = null,
        IInngestClient? client = null)
    {
        Event = evt;
        Events = events;
        Steps = steps ?? new Dictionary<string, object>();
        Ctx = ctx;
        _secrets = secrets ?? new Dictionary<string, string>();
        _client = client;
    }

    /// <summary>
    /// Executes a step with optional retry configuration
    /// </summary>
    /// <typeparam name="T">The return type of the step</typeparam>
    /// <param name="id">Unique identifier for the step</param>
    /// <param name="action">The function to execute for this step</param>
    /// <param name="options">Optional configuration for the step execution</param>
    /// <returns>The result of the step execution</returns>
    public async Task<T> Step<T>(string id, Func<Task<T>> action, StepOptions? options = null)
    {
        // If the step has already been executed, return the result
        if (Steps.TryGetValue(id, out var stepResult))
        {
            if (stepResult is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? throw new InvalidOperationException($"Failed to deserialize step result for '{id}'");
            }
            return (T)stepResult;
        }

        // Configure retry logic if specified
        int retryAttempt = 0;
        int maxRetries = options?.Retry?.Attempts ?? 0;
        double retryFactor = options?.Retry?.Factor ?? 2.0;
        int baseInterval = options?.Retry?.Interval ?? 1000; // milliseconds
        int maxInterval = options?.Retry?.MaxInterval ?? 30000; // milliseconds

        while (true)
        {
            try
            {
                var result = await action();
                Steps[id] = result!;
                return result;
            }
            catch (Exception ex)
            {
                if (retryAttempt >= maxRetries)
                {
                    throw new StepExecutionException($"Step '{id}' failed after {retryAttempt + 1} attempts", ex);
                }

                // Calculate backoff time
                int delayMs = (int)Math.Min(
                    baseInterval * Math.Pow(retryFactor, retryAttempt),
                    maxInterval
                );

                await Task.Delay(delayMs);
                retryAttempt++;
            }
        }
    }

    /// <summary>
    /// Introduces a delay in the function execution
    /// </summary>
    /// <param name="id">Unique identifier for the sleep step</param>
    /// <param name="duration">The duration to sleep</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task Sleep(string id, TimeSpan duration)
    {
        await Step(id, async () =>
        {
            await Task.Delay(duration);
            return true;
        });
    }

    /// <summary>
    /// Retrieves a secret value by key
    /// </summary>
    /// <param name="key">The key of the secret to retrieve</param>
    /// <returns>The secret value</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the specified key is not found</exception>
    public string GetSecret(string key)
    {
        if (_secrets.TryGetValue(key, out var value))
        {
            return value;
        }
        
        throw new KeyNotFoundException($"Secret '{key}' not found");
    }

    /// <summary>
    /// Sends an event to Inngest
    /// </summary>
    /// <param name="eventName">The name of the event</param>
    /// <param name="data">The data payload for the event</param>
    /// <returns>True if the event was sent successfully, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not available</exception>
    public async Task<bool> SendEvent(string eventName, object data)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("InngestClient not available in this context");
        }
        
        return await _client.SendEventAsync(eventName, data);
    }
    
    /// <summary>
    /// Sends a pre-configured event to Inngest
    /// </summary>
    /// <param name="evt">The event to send</param>
    /// <returns>True if the event was sent successfully, otherwise false</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not available</exception>
    public async Task<bool> SendEvent(InngestEvent evt)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("InngestClient not available in this context");
        }
        
        return await _client.SendEventAsync(evt);
    }
}

/// <summary>
/// Configuration options for step execution
/// </summary>
public class StepOptions
{
    /// <summary>
    /// Optional display name for the step
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// Retry configuration for the step
    /// </summary>
    public RetryOptions? Retry { get; set; }
}

/// <summary>
/// Exception thrown when a step execution fails
/// </summary>
public class StepExecutionException : Exception
{
    /// <summary>
    /// Creates a new StepExecutionException with the specified message and inner exception
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception that caused this exception</param>
    public StepExecutionException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}