using System.Text.Json;
using Inngest.Steps;

namespace Inngest;

/// <summary>
/// Interface for handling function failures after all retries are exhausted.
/// Implementations can perform cleanup, send notifications, or log to external services.
/// </summary>
public interface IInngestFailureHandler
{
    /// <summary>
    /// Called when the parent function fails after all retry attempts.
    /// </summary>
    /// <param name="context">Context containing failure information and step tools</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleFailureAsync(FailureContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context provided to failure handlers with information about the failed function run.
/// </summary>
public class FailureContext
{
    /// <summary>
    /// Information about the function failure
    /// </summary>
    public FunctionFailureInfo Failure { get; }

    /// <summary>
    /// The original event that triggered the failed function
    /// </summary>
    public InngestEvent OriginalEvent { get; }

    /// <summary>
    /// Step tools for building durable workflows in the failure handler.
    /// Can be used to send events, invoke other functions, etc.
    /// </summary>
    public IStepTools Step { get; }

    /// <summary>
    /// Run context containing metadata about this failure handler execution
    /// </summary>
    public RunContext Run { get; }

    /// <summary>
    /// Logger scoped to this failure handler execution
    /// </summary>
    public Microsoft.Extensions.Logging.ILogger Logger { get; }

    /// <summary>
    /// Cancellation token for this execution
    /// </summary>
    public CancellationToken CancellationToken { get; }

    internal FailureContext(
        FunctionFailureInfo failure,
        InngestEvent originalEvent,
        IStepTools stepTools,
        RunContext runContext,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        Failure = failure;
        OriginalEvent = originalEvent;
        Step = stepTools;
        Run = runContext;
        Logger = logger;
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Information about a function failure
/// </summary>
public class FunctionFailureInfo
{
    /// <summary>
    /// The ID of the function that failed
    /// </summary>
    public string FunctionId { get; init; } = string.Empty;

    /// <summary>
    /// The unique run ID of the failed execution
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// Error information from the failure
    /// </summary>
    public FunctionError Error { get; init; } = new();

    /// <summary>
    /// Creates an exception from this failure info for Sentry/logging integration
    /// </summary>
    public InngestFunctionFailedException ToException()
    {
        return new InngestFunctionFailedException(FunctionId, RunId, Error);
    }
}

/// <summary>
/// Error details from a function failure
/// </summary>
public class FunctionError
{
    /// <summary>
    /// The error type/name (e.g., "Error", "TimeoutError")
    /// </summary>
    public string Name { get; init; } = "Error";

    /// <summary>
    /// The error message
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Stack trace if available
    /// </summary>
    public string? Stack { get; init; }
}

/// <summary>
/// Exception representing a failed Inngest function, suitable for Sentry capture.
/// </summary>
public class InngestFunctionFailedException : Exception
{
    /// <summary>
    /// The ID of the function that failed
    /// </summary>
    public string FunctionId { get; }

    /// <summary>
    /// The run ID of the failed execution
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// The error details
    /// </summary>
    public FunctionError Error { get; }

    /// <summary>
    /// Creates a new InngestFunctionFailedException
    /// </summary>
    public InngestFunctionFailedException(string functionId, string runId, FunctionError error)
        : base($"Inngest function '{functionId}' failed: {error.Message}")
    {
        FunctionId = functionId;
        RunId = runId;
        Error = error;
    }

    /// <inheritdoc/>
    public override string? StackTrace => Error.Stack ?? base.StackTrace;
}
