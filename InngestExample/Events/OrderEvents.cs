using Inngest;

namespace InngestExample.Events;

/// <summary>
/// Event data for order created events.
/// Implements IInngestEventData for compile-time type safety.
/// </summary>
/// <remarks>
/// The EventName is used automatically when:
/// - Sending events via client.SendAsync(new OrderCreatedEvent { ... })
/// - Deriving triggers for functions that use IInngestFunction&lt;OrderCreatedEvent&gt;
/// </remarks>
public record OrderCreatedEvent : IInngestEventData
{
    /// <summary>
    /// The event name used when sending this event to Inngest.
    /// </summary>
    public static string EventName => "shop/order.created";

    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string CustomerId { get; init; }
}

/// <summary>
/// Payment confirmation event from Stripe webhook.
/// </summary>
public record PaymentConfirmedEvent : IInngestEventData
{
    public static string EventName => "stripe/payment.succeeded";

    public required string OrderId { get; init; }
    public required string TransactionId { get; init; }
    public required decimal Amount { get; init; }
    public required bool Success { get; init; }
}

/// <summary>
/// Event data for user signup events.
/// </summary>
public record UserSignedUpEvent : IInngestEventData
{
    public static string EventName => "user/signed.up";

    public required string UserId { get; init; }
    public required string Email { get; init; }
    public string? Name { get; init; }
}
