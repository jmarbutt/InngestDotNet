using Inngest.Steps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inngest;

/// <summary>
/// Execution context for Inngest functions.
/// Provides access to the triggering event, step tools for building durable workflows,
/// and run metadata.
/// </summary>
public class InngestContext
{
    /// <summary>
    /// The event that triggered this function execution
    /// </summary>
    public InngestEvent Event { get; }

    /// <summary>
    /// All events in the batch (for batched triggers).
    /// For non-batched triggers, this contains just the single triggering event.
    /// </summary>
    public IReadOnlyList<InngestEvent> Events { get; }

    /// <summary>
    /// Step tools for building durable workflows.
    /// Use this to run code with automatic retries, sleep, wait for events, etc.
    /// </summary>
    public IStepTools Step { get; }

    /// <summary>
    /// Run context containing metadata about the current execution
    /// </summary>
    public RunContext Run { get; }

    /// <summary>
    /// Logger scoped to this function run
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Cancellation token for the current execution.
    /// Check this token periodically in long-running operations.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Creates a new InngestContext
    /// </summary>
    internal InngestContext(
        InngestEvent evt,
        IEnumerable<InngestEvent> events,
        IStepTools stepTools,
        RunContext runContext,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        Event = evt;
        Events = events.ToList().AsReadOnly();
        Step = stepTools;
        Run = runContext;
        Logger = logger ?? NullLogger.Instance;
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Strongly-typed execution context for Inngest functions.
/// Provides typed access to event data.
/// </summary>
/// <typeparam name="TEventData">The type of the event data</typeparam>
public class InngestContext<TEventData> : InngestContext where TEventData : class
{
    /// <summary>
    /// The typed event that triggered this function
    /// </summary>
    public new InngestEvent<TEventData> Event { get; }

    /// <summary>
    /// Creates a new typed InngestContext
    /// </summary>
    internal InngestContext(
        InngestEvent<TEventData> evt,
        IEnumerable<InngestEvent> events,
        IStepTools stepTools,
        RunContext runContext,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        : base(evt, events, stepTools, runContext, logger, cancellationToken)
    {
        Event = evt;
    }
}

/// <summary>
/// Context information about the current function run
/// </summary>
public class RunContext
{
    /// <summary>
    /// Unique identifier for this run
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// The function ID being executed
    /// </summary>
    public string FunctionId { get; init; } = string.Empty;

    /// <summary>
    /// The current attempt number (0-indexed).
    /// First attempt = 0, second attempt = 1, etc.
    /// </summary>
    /// <remarks>
    /// When using with MaxAttempts, note that:
    /// - If MaxAttempts = 3, valid Attempt values are 0, 1, 2
    /// - IsFinalAttempt is true when Attempt == MaxAttempts - 1
    /// </remarks>
    public int Attempt { get; init; }

    /// <summary>
    /// Maximum number of attempts configured for this function.
    /// This is the total number of times the function will be executed
    /// (first attempt + retries).
    /// </summary>
    /// <remarks>
    /// This value comes from the [Retry(Attempts = N)] attribute.
    /// If not specified, defaults to the Inngest platform default (typically 4).
    /// </remarks>
    public int MaxAttempts { get; init; } = 4; // Inngest default

    /// <summary>
    /// Whether this is a replay (re-execution with memoized state)
    /// </summary>
    public bool IsReplay { get; init; }

    /// <summary>
    /// Returns true if this is the final attempt (no more retries will occur).
    /// Use this to perform terminal actions like sending failure notifications.
    /// </summary>
    /// <remarks>
    /// This is equivalent to checking Attempt == MaxAttempts - 1.
    /// If the function succeeds on this attempt, no failure handlers run.
    /// If it fails, the onFailure handler (if configured) will be triggered.
    /// </remarks>
    public bool IsFinalAttempt => Attempt >= MaxAttempts - 1;
}

/// <summary>
/// Typed event with strongly-typed data property
/// </summary>
/// <typeparam name="TData">The type of the event data</typeparam>
public class InngestEvent<TData> : InngestEvent where TData : class
{
    /// <summary>
    /// The strongly-typed event data
    /// </summary>
    public new TData? Data
    {
        get => base.Data as TData;
        set => base.Data = value;
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
/// Exception thrown when a step execution fails after exhausting retries.
/// This is kept for backwards compatibility.
/// </summary>
public class StepExecutionException : Exception
{
    /// <summary>
    /// Gets whether this exception should not be retried
    /// </summary>
    public bool NoRetry { get; }

    /// <summary>
    /// Gets the amount of time to wait before retrying
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates a new StepExecutionException
    /// </summary>
    public StepExecutionException(string message, Exception innerException, bool noRetry = false)
        : base(message, innerException)
    {
        NoRetry = noRetry;
    }

    /// <summary>
    /// Creates a new StepExecutionException with retry delay
    /// </summary>
    public StepExecutionException(string message, Exception innerException, TimeSpan retryAfter)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Creates a new StepExecutionException
    /// </summary>
    public StepExecutionException(string message, bool noRetry = false)
        : base(message)
    {
        NoRetry = noRetry;
    }
}
