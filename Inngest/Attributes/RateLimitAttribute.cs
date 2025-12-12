namespace Inngest.Attributes;

/// <summary>
/// Configures rate limiting for an Inngest function.
/// </summary>
/// <example>
/// <code>
/// // Limit to 100 executions per hour
/// [InngestFunction("api-caller")]
/// [EventTrigger("api/call.requested")]
/// [RateLimit(100, "1h")]
/// public class ApiCaller : IInngestFunction { }
///
/// // Limit to 10 executions per minute per customer
/// [InngestFunction("customer-processor")]
/// [EventTrigger("customer/action")]
/// [RateLimit(10, "1m", Key = "event.data.customerId")]
/// public class CustomerProcessor : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RateLimitAttribute : Attribute
{
    /// <summary>
    /// The maximum number of executions allowed in the period
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// The time period for the rate limit.
    /// Uses Inngest time string format (e.g., "1m", "1h", "1d").
    /// </summary>
    public string Period { get; }

    /// <summary>
    /// Optional CEL expression to partition the rate limit.
    /// When specified, the limit applies per unique value of this expression.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Creates a new rate limit attribute
    /// </summary>
    /// <param name="limit">The maximum number of executions in the period</param>
    /// <param name="period">The time period (e.g., "1m", "1h", "1d")</param>
    public RateLimitAttribute(int limit, string period)
    {
        if (limit < 1)
            throw new ArgumentException("Rate limit must be at least 1", nameof(limit));
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Period cannot be null or empty", nameof(period));

        Limit = limit;
        Period = period;
    }
}
