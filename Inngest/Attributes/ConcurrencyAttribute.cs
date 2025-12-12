namespace Inngest.Attributes;

/// <summary>
/// Configures concurrency limits for an Inngest function.
/// </summary>
/// <example>
/// <code>
/// // Limit to 5 concurrent executions globally
/// [InngestFunction("processor")]
/// [EventTrigger("task/created")]
/// [Concurrency(5)]
/// public class Processor : IInngestFunction { }
///
/// // Limit to 1 concurrent execution per user
/// [InngestFunction("user-processor")]
/// [EventTrigger("user/task.created")]
/// [Concurrency(1, Key = "event.data.userId")]
/// public class UserProcessor : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ConcurrencyAttribute : Attribute
{
    /// <summary>
    /// The maximum number of concurrent executions
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// Optional CEL expression to partition concurrency.
    /// When specified, the limit applies per unique value of this expression.
    /// </summary>
    /// <example>
    /// <code>
    /// // Limit to 1 concurrent execution per user
    /// [Concurrency(1, Key = "event.data.userId")]
    /// </code>
    /// </example>
    public string? Key { get; set; }

    /// <summary>
    /// The scope for concurrency limiting.
    /// "fn" (default) applies to this function only.
    /// "env" applies across all functions in the environment.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Creates a new concurrency attribute
    /// </summary>
    /// <param name="limit">The maximum number of concurrent executions</param>
    public ConcurrencyAttribute(int limit)
    {
        if (limit < 1)
            throw new ArgumentException("Concurrency limit must be at least 1", nameof(limit));

        Limit = limit;
    }
}
