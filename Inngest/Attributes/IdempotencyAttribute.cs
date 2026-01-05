namespace Inngest.Attributes;

/// <summary>
/// Configures idempotency for an Inngest function using a CEL expression.
/// When multiple events result in the same idempotency key, only the first
/// event will be processed; subsequent events with the same key are skipped.
/// </summary>
/// <remarks>
/// Use idempotency to prevent duplicate processing when events may be retried
/// or delivered multiple times, such as webhook handlers or receipt senders.
/// </remarks>
/// <example>
/// <code>
/// // One receipt per contribution - prevents duplicate emails on retry
/// [InngestFunction("send-donor-receipt")]
/// [EventTrigger("contribution/created")]
/// [Idempotency("event.data.contributionId")]
/// public class SendDonorReceiptFunction : IInngestFunction { }
///
/// // Compound key for more specific deduplication
/// [InngestFunction("process-payment")]
/// [EventTrigger("payment/received")]
/// [Idempotency("event.data.paymentId + '-' + event.data.customerId")]
/// public class ProcessPaymentFunction : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IdempotencyAttribute : Attribute
{
    /// <summary>
    /// CEL expression that evaluates to the idempotency key.
    /// Events with the same key will only be processed once.
    /// </summary>
    public string Key { get; }

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
