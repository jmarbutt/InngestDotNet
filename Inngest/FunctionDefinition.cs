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
    /// Maximum number of concurrent executions allowed
    /// </summary>
    public int? Concurrency { get; set; }
    
    /// <summary>
    /// Retry configuration for failed executions
    /// </summary>
    public RetryOptions? Retry { get; set; }
    
    /// <summary>
    /// Custom queue name for the function
    /// </summary>
    public string? Queue { get; set; }
    
    /// <summary>
    /// Environment variables to inject into the function
    /// </summary>
    public Dictionary<string, string>? Env { get; set; }
    
    /// <summary>
    /// Rate limit for function execution
    /// </summary>
    public int? RateLimit { get; set; }
    
    /// <summary>
    /// Idempotency settings for the function
    /// </summary>
    public Dictionary<string, string>? Idempotency { get; set; }
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
