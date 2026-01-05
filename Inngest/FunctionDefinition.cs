namespace Inngest;

/// <summary>
/// Defines an Inngest function with triggers and execution handler
/// </summary>
public class FunctionDefinition
{
    /// <summary>
    /// Unique identifier for the function
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the function
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Array of triggers that will cause this function to execute
    /// </summary>
    public FunctionTrigger[] Triggers { get; set; } = Array.Empty<FunctionTrigger>();

    /// <summary>
    /// Optional configuration options for this function
    /// </summary>
    public FunctionOptions? Options { get; set; }

    /// <summary>
    /// The handler function that will be executed when triggered
    /// </summary>
    public Func<InngestContext, Task<object>> Handler { get; set; } = null!;

    /// <summary>
    /// List of steps defined for this function
    /// </summary>
    public List<StepDefinition> Steps { get; } = new();

    /// <summary>
    /// Create a new function definition
    /// </summary>
    public FunctionDefinition(string id, string name, FunctionTrigger[] triggers, Func<InngestContext, Task<object>> handler, FunctionOptions? options = null)
    {
        Id = id;
        Name = name;
        Triggers = triggers;
        Handler = handler;
        Options = options;
    }

    /// <summary>
    /// Adds a step definition to this function
    /// </summary>
    public void AddStep(string id, string? name = null, RetryOptions? retryOptions = null)
    {
        Steps.Add(new StepDefinition(id, name, retryOptions));
    }
}

/// <summary>
/// Defines a trigger condition for an Inngest function
/// </summary>
public class FunctionTrigger
{
    /// <summary>
    /// The event name or cron expression that triggers the function
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Optional constraint to filter events more specifically
    /// </summary>
    public EventConstraint? Constraint { get; set; }

    /// <summary>
    /// Create a trigger for a specific event name
    /// </summary>
    public static FunctionTrigger CreateEventTrigger(string eventName)
    {
        return new FunctionTrigger { Event = eventName };
    }

    /// <summary>
    /// Create a trigger using a cron schedule
    /// </summary>
    public static FunctionTrigger CreateCronTrigger(string cronExpression)
    {
        return new FunctionTrigger { Event = $"cron({cronExpression})" };
    }
}

/// <summary>
/// Defines a constraint for filtering events
/// </summary>
public class EventConstraint
{
    /// <summary>
    /// The CEL expression for filtering events
    /// </summary>
    public string Expression { get; set; } = string.Empty;
}

/// <summary>
/// Configuration options for a function
/// </summary>
public class FunctionOptions
{
    /// <summary>
    /// Maximum number of concurrent executions allowed (simple limit)
    /// </summary>
    public int? Concurrency { get; set; }

    /// <summary>
    /// Advanced concurrency configuration with key and scope
    /// </summary>
    public ConcurrencyOptions? ConcurrencyOptions { get; set; }

    /// <summary>
    /// Retry configuration for failed executions
    /// </summary>
    public RetryOptions? Retry { get; set; }

    /// <summary>
    /// Rate limit configuration
    /// </summary>
    public RateLimitOptions? RateLimit { get; set; }

    /// <summary>
    /// Throttle configuration (limit executions over a time period)
    /// </summary>
    public ThrottleOptions? Throttle { get; set; }

    /// <summary>
    /// Debounce configuration (delay execution until events stop arriving)
    /// </summary>
    public DebounceOptions? Debounce { get; set; }

    /// <summary>
    /// Batch configuration (group multiple events into a single execution)
    /// </summary>
    public BatchOptions? Batch { get; set; }

    /// <summary>
    /// Priority for function execution (1-10, lower = higher priority)
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Cancellation triggers - events that will cancel running functions
    /// </summary>
    public CancellationOptions? Cancellation { get; set; }

    /// <summary>
    /// Idempotency key expression (CEL) to prevent duplicate executions
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Timeout configuration to automatically cancel runs that take too long
    /// </summary>
    public TimeoutOptions? Timeouts { get; set; }
}

/// <summary>
/// Advanced concurrency configuration
/// </summary>
public class ConcurrencyOptions
{
    /// <summary>
    /// Maximum number of concurrent executions
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// CEL expression to generate a concurrency key (e.g., "event.data.userId")
    /// Functions with the same key share the concurrency limit
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Scope of the concurrency limit: "fn" (per function) or "env" (per environment)
    /// </summary>
    public string? Scope { get; set; }
}

/// <summary>
/// Rate limit configuration
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// Maximum number of executions allowed in the period
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Time period for the rate limit (e.g., "1m", "1h", "1d")
    /// </summary>
    public string Period { get; set; } = "1m";

    /// <summary>
    /// CEL expression to generate a rate limit key (e.g., "event.data.userId")
    /// </summary>
    public string? Key { get; set; }
}

/// <summary>
/// Throttle configuration - limits function execution rate
/// </summary>
public class ThrottleOptions
{
    /// <summary>
    /// Maximum number of executions in the period
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Time period for throttling (e.g., "1m", "1h")
    /// </summary>
    public string Period { get; set; } = "1m";

    /// <summary>
    /// CEL expression to generate a throttle key
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Maximum number of events to burst through before throttling
    /// </summary>
    public int? Burst { get; set; }
}

/// <summary>
/// Debounce configuration - delays execution until events stop arriving
/// </summary>
public class DebounceOptions
{
    /// <summary>
    /// Time to wait for more events before executing (e.g., "5s", "1m")
    /// </summary>
    public string Period { get; set; } = "5s";

    /// <summary>
    /// CEL expression to generate a debounce key (events with same key are debounced together)
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Maximum time to wait before executing regardless of new events
    /// </summary>
    public string? Timeout { get; set; }
}

/// <summary>
/// Batch configuration - groups multiple events into a single execution
/// </summary>
public class BatchOptions
{
    /// <summary>
    /// Maximum number of events to batch together
    /// </summary>
    public int MaxSize { get; set; }

    /// <summary>
    /// Maximum time to wait for events before executing (e.g., "5s", "1m")
    /// </summary>
    public string? Timeout { get; set; }

    /// <summary>
    /// CEL expression to generate a batch key (events with same key are batched together)
    /// </summary>
    public string? Key { get; set; }
}

/// <summary>
/// Cancellation configuration - allows cancelling running functions
/// </summary>
public class CancellationOptions
{
    /// <summary>
    /// Event name that triggers cancellation
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// CEL expression to match cancellation events (e.g., "async.data.orderId == event.data.orderId")
    /// </summary>
    public string? Match { get; set; }

    /// <summary>
    /// CEL expression condition for when to cancel
    /// </summary>
    public string? If { get; set; }

    /// <summary>
    /// Timeout after which to cancel (e.g., "1h")
    /// </summary>
    public string? Timeout { get; set; }
}

/// <summary>
/// Timeout configuration to automatically cancel runs that take too long
/// </summary>
public class TimeoutOptions
{
    /// <summary>
    /// Maximum time a run can wait in the queue before starting.
    /// Uses Inngest time string format (e.g., "10s", "1m", "1h").
    /// If exceeded, the run is cancelled before it starts.
    /// </summary>
    public string? Start { get; set; }

    /// <summary>
    /// Maximum time a run can execute after starting.
    /// Uses Inngest time string format (e.g., "30s", "5m", "1h").
    /// If exceeded, the run is cancelled during execution.
    /// </summary>
    public string? Finish { get; set; }
}

/// <summary>
/// Configuration for retry behavior
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int? Attempts { get; set; }

    /// <summary>
    /// Base interval between retries in milliseconds
    /// </summary>
    public int? Interval { get; set; }

    /// <summary>
    /// Exponential backoff factor for retries
    /// </summary>
    public double? Factor { get; set; }

    /// <summary>
    /// Maximum interval between retries in milliseconds
    /// </summary>
    public int? MaxInterval { get; set; }
}

/// <summary>
/// Defines a step within an Inngest function
/// </summary>
public class StepDefinition
{
    /// <summary>
    /// The ID of the step
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name for the step (optional)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Retry configuration for the step
    /// </summary>
    public RetryOptions? RetryOptions { get; set; }

    /// <summary>
    /// Create a new step definition
    /// </summary>
    public StepDefinition(string id, string? name = null, RetryOptions? retryOptions = null)
    {
        Id = id;
        Name = name;
        RetryOptions = retryOptions;
    }
}
