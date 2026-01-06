namespace Inngest.Attributes;

/// <summary>
/// Configures idempotency for an Inngest function using a CEL expression.
/// When multiple events result in the same idempotency key, only the first
/// event will be processed; subsequent events with the same key are skipped
/// within the specified time-to-live period.
/// </summary>
/// <remarks>
/// Use idempotency to prevent duplicate processing when events may be retried
/// or delivered multiple times, such as webhook handlers or receipt senders.
/// </remarks>
/// <example>
/// <code>
/// // One receipt per contribution - prevents duplicate emails on retry (default TTL)
/// [InngestFunction("send-donor-receipt")]
/// [EventTrigger("contribution/created")]
/// [Idempotency("event.data.contributionId")]
/// public class SendDonorReceiptFunction : IInngestFunction { }
///
/// // With explicit 24-hour TTL
/// [InngestFunction("process-payment")]
/// [EventTrigger("payment/received")]
/// [Idempotency("event.data.paymentId", Period = "24h")]
/// public class ProcessPaymentFunction : IInngestFunction { }
///
/// // Compound key with 1-hour TTL
/// [InngestFunction("sync-contribution")]
/// [EventTrigger("contribution/sync")]
/// [Idempotency("event.data.contributionId + '-' + event.data.source", Period = "1h")]
/// public class SyncContributionFunction : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IdempotencyAttribute : Attribute
{
    /// <summary>
    /// CEL expression that evaluates to the idempotency key.
    /// Events with the same key will only be processed once within the TTL period.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Time-to-live period for the idempotency key.
    /// Uses Inngest time string format (e.g., "1h", "24h", "7d").
    /// After this period expires, the same key can trigger a new execution.
    /// If not specified, Inngest uses its default TTL.
    /// </summary>
    public string? Period { get; set; }

    /// <summary>
    /// Creates a new idempotency attribute
    /// </summary>
    /// <param name="key">CEL expression for the idempotency key (e.g., "event.data.orderId")</param>
    public IdempotencyAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(key));

        Key = key;
    }
}
