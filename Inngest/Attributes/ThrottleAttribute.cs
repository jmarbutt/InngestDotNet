namespace Inngest.Attributes;

/// <summary>
/// Configures throttling for an Inngest function.
/// Unlike RateLimit which DROPS events when the limit is exceeded,
/// Throttle QUEUES events and processes them at the configured rate.
/// </summary>
/// <remarks>
/// Use Throttle instead of RateLimit when you cannot afford to lose events,
/// such as payment processing webhooks.
/// </remarks>
/// <example>
/// <code>
/// // Limit to 20 executions per minute, queuing excess events
/// [InngestFunction("payment-processor")]
/// [EventTrigger("payment/received")]
/// [Throttle(20, "1m")]
/// public class PaymentProcessor : IInngestFunction { }
///
/// // Limit to 10 executions per minute per customer, with burst allowance
/// [InngestFunction("customer-processor")]
/// [EventTrigger("customer/action")]
/// [Throttle(10, "1m", Key = "event.data.customerId", Burst = 5)]
/// public class CustomerProcessor : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ThrottleAttribute : Attribute
{
    /// <summary>
    /// The maximum number of executions allowed in the period
    /// </summary>
    public int Limit { get; }

    /// <summary>
    /// The time period for throttling.
    /// Uses Inngest time string format (e.g., "1m", "1h", "1d").
    /// </summary>
    public string Period { get; }

    /// <summary>
    /// Optional CEL expression to partition the throttle.
    /// When specified, the limit applies per unique value of this expression.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Maximum number of events to burst through before throttling kicks in.
    /// </summary>
    public int? Burst { get; set; }

    /// <summary>
    /// Creates a new throttle attribute
    /// </summary>
    /// <param name="limit">The maximum number of executions in the period</param>
    /// <param name="period">The time period (e.g., "1m", "1h", "1d")</param>
    public ThrottleAttribute(int limit, string period)
    {
        if (limit < 1)
            throw new ArgumentException("Throttle limit must be at least 1", nameof(limit));
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Period cannot be null or empty", nameof(period));

        Limit = limit;
        Period = period;
    }
}
