using Inngest;
using Inngest.Attributes;
using Inngest.Exceptions;

namespace InngestExample.Functions;

/// <summary>
/// Example event for testing step errors
/// </summary>
public record TestStepErrorEvent : IInngestEventData
{
    public static string EventName => "test/step-error";

    /// <summary>
    /// Which step should fail: "first", "second", or "none"
    /// </summary>
    public string? FailAt { get; init; }

    /// <summary>
    /// If true, the error is non-retriable
    /// </summary>
    public bool? NonRetriable { get; init; }
}

/// <summary>
/// Example function that demonstrates step error handling and retries.
///
/// This function shows how the SDK handles step failures:
/// 1. When a step throws an exception, the SDK returns HTTP 500 to trigger Inngest's retry mechanism
/// 2. Inngest will retry the function up to the configured number of attempts
/// 3. Previously successful steps are memoized and not re-executed on retry
///
/// To test:
/// - Send event "test/step-error" with data { "failAt": "first" } to see first step fail
/// - Send event "test/step-error" with data { "failAt": "second" } to see second step fail (after first succeeds)
/// - Send event "test/step-error" with data { "failAt": "none" } to see successful execution
/// - Send event "test/step-error" with data { "failAt": "first", "nonRetriable": true } to see non-retriable error
/// </summary>
[InngestFunction("step-error-example", Name = "Step Error Example")]
[Retry(Attempts = 3)]
public class StepErrorExampleFunction : IInngestFunction<TestStepErrorEvent>
{
    private readonly ILogger<StepErrorExampleFunction> _logger;

    public StepErrorExampleFunction(ILogger<StepErrorExampleFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext<TestStepErrorEvent> context, CancellationToken cancellationToken)
    {
        var eventData = context.Event.Data;
        var failAt = eventData?.FailAt?.ToLowerInvariant() ?? "none";
        var nonRetriable = eventData?.NonRetriable ?? false;

        _logger.LogInformation("Starting step error example - failAt: {FailAt}, nonRetriable: {NonRetriable}, attempt: {Attempt}",
            failAt, nonRetriable, context.Run.Attempt);

        // Step 1: First step
        var step1Result = await context.Step.Run("first-step", () =>
        {
            _logger.LogInformation("Executing first step...");

            if (failAt == "first")
            {
                if (nonRetriable)
                {
                    throw new NonRetriableException("First step failed with non-retriable error");
                }
                throw new InvalidOperationException("First step failed - this error will trigger a retry");
            }

            return new { step = 1, status = "success", timestamp = DateTime.UtcNow };
        });

        _logger.LogInformation("First step completed: {Result}", step1Result);

        // Step 2: Second step (only executes if first succeeds)
        var step2Result = await context.Step.Run("second-step", async () =>
        {
            _logger.LogInformation("Executing second step...");
            await Task.Delay(100, cancellationToken);

            if (failAt == "second")
            {
                if (nonRetriable)
                {
                    throw new NonRetriableException("Second step failed with non-retriable error");
                }
                throw new ArgumentException("Second step failed - this error will trigger a retry");
            }

            return new { step = 2, status = "success", previousStep = step1Result.status };
        });

        _logger.LogInformation("Second step completed: {Result}", step2Result);

        // Step 3: Final step
        var finalResult = await context.Step.Run("final-step", () =>
        {
            _logger.LogInformation("Executing final step...");
            return new
            {
                step = 3,
                status = "completed",
                totalSteps = 3,
                completedAt = DateTime.UtcNow
            };
        });

        return new
        {
            success = true,
            attempt = context.Run.Attempt,
            steps = new object[] { step1Result, step2Result, finalResult }
        };
    }
}

/// <summary>
/// Example function that demonstrates transient failures that succeed after retries.
///
/// This simulates real-world scenarios like:
/// - External API temporarily unavailable
/// - Database connection timeout
/// - Rate limiting
///
/// The function fails on the first 2 attempts but succeeds on the 3rd.
/// </summary>
public record TestTransientErrorEvent : IInngestEventData
{
    public static string EventName => "test/transient-error";
    public string? Message { get; init; }
}

[InngestFunction("transient-error-example", Name = "Transient Error Example")]
[Retry(Attempts = 5)]
public class TransientErrorExampleFunction : IInngestFunction<TestTransientErrorEvent>
{
    private readonly ILogger<TransientErrorExampleFunction> _logger;

    public TransientErrorExampleFunction(ILogger<TransientErrorExampleFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext<TestTransientErrorEvent> context, CancellationToken cancellationToken)
    {
        var attempt = context.Run.Attempt;
        _logger.LogInformation("Transient error example - attempt {Attempt}", attempt);

        // Step 1: Simulate external API call that fails transiently
        var apiResult = await context.Step.Run("call-external-api", () =>
        {
            _logger.LogInformation("Calling external API (attempt {Attempt})...", attempt);

            // Simulate transient failure - fails on first 2 attempts
            if (attempt < 2)
            {
                throw new HttpRequestException($"External API temporarily unavailable (attempt {attempt})");
            }

            return new
            {
                status = "success",
                data = "API response data",
                attempt = attempt
            };
        });

        // Step 2: Process the API response
        var processed = await context.Step.Run("process-response", () =>
        {
            _logger.LogInformation("Processing API response...");
            return new
            {
                processed = true,
                originalData = apiResult.data
            };
        });

        return new
        {
            success = true,
            message = context.Event.Data?.Message ?? "Completed successfully after transient failures",
            apiResult,
            processed,
            totalAttempts = attempt + 1
        };
    }
}

/// <summary>
/// Example function demonstrating retry-after delays.
///
/// Uses RetryAfterException to tell Inngest to wait a specific amount of time before retrying.
/// Useful for rate limiting scenarios.
/// </summary>
public record TestRetryAfterEvent : IInngestEventData
{
    public static string EventName => "test/retry-after";
    public int? DelaySeconds { get; init; }
}

[InngestFunction("retry-after-example", Name = "Retry After Example")]
[Retry(Attempts = 3)]
public class RetryAfterExampleFunction : IInngestFunction<TestRetryAfterEvent>
{
    private readonly ILogger<RetryAfterExampleFunction> _logger;

    public RetryAfterExampleFunction(ILogger<RetryAfterExampleFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext<TestRetryAfterEvent> context, CancellationToken cancellationToken)
    {
        var attempt = context.Run.Attempt;
        var delaySeconds = context.Event.Data?.DelaySeconds ?? 30;

        _logger.LogInformation("Retry-after example - attempt {Attempt}", attempt);

        var result = await context.Step.Run("rate-limited-api-call", () =>
        {
            _logger.LogInformation("Making rate-limited API call (attempt {Attempt})...", attempt);

            // Simulate rate limiting on first attempt
            if (attempt < 1)
            {
                throw new RetryAfterException(
                    $"Rate limited - retry after {delaySeconds} seconds",
                    TimeSpan.FromSeconds(delaySeconds));
            }

            return new
            {
                status = "success",
                message = "API call completed",
                attempt = attempt
            };
        });

        return new
        {
            success = true,
            result,
            totalAttempts = attempt + 1
        };
    }
}
