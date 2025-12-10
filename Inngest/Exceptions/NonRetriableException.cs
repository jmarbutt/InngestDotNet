namespace Inngest.Exceptions;

/// <summary>
/// Exception that indicates an error that should not be retried.
/// When this exception is thrown, Inngest will NOT retry the function
/// and will mark the run as failed immediately.
///
/// Use this for:
/// - Validation errors (invalid input that won't change on retry)
/// - Business logic errors (conditions that won't change)
/// - Permanent failures (deleted resources, revoked permissions)
/// </summary>
public class NonRetriableException : Exception
{
    /// <summary>
    /// Creates a new NonRetriableException
    /// </summary>
    /// <param name="message">The error message</param>
    public NonRetriableException(string message) : base(message) { }

    /// <summary>
    /// Creates a new NonRetriableException with an inner exception
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public NonRetriableException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Exception that indicates an error that should be retried after a specific delay.
/// </summary>
public class RetryAfterException : Exception
{
    /// <summary>
    /// The delay before the function should be retried
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// Creates a new RetryAfterException
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="retryAfter">The delay before retry</param>
    public RetryAfterException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Creates a new RetryAfterException with an inner exception
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="retryAfter">The delay before retry</param>
    /// <param name="innerException">The inner exception</param>
    public RetryAfterException(string message, TimeSpan retryAfter, Exception innerException)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }
}
