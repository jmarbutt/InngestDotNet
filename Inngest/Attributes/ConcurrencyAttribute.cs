namespace Inngest.Attributes;

/// <summary>
/// Configures concurrency limits for an Inngest function.
/// Multiple attributes can be applied to create compound concurrency constraints
/// (e.g., per-key serialization combined with a global cap).
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
///
/// // Multiple constraints: per-payment serialization + global cap
/// [InngestFunction("payment-processor")]
/// [EventTrigger("payment/created")]
/// [Concurrency(1, Key = "event.data.paymentId")]  // Per-key serialization
/// [Concurrency(5)]                                  // Global cap for DB protection
/// public class PaymentProcessor : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
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
