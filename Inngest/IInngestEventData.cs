namespace Inngest;

/// <summary>
/// Interface for strongly-typed Inngest event data.
/// Implement this interface to define events with compile-time type safety.
/// </summary>
/// <remarks>
/// This interface uses C# 11's static abstract members to provide the event name
/// at compile time, enabling fully type-safe event sending without magic strings.
/// </remarks>
/// <example>
/// <code>
/// public record OrderCreatedEvent : IInngestEventData
/// {
///     public static string EventName => "shop/order.created";
///
///     public required string OrderId { get; init; }
///     public required decimal Amount { get; init; }
/// }
///
/// // Usage - fully typed, no magic strings
/// await inngestClient.SendAsync(new OrderCreatedEvent
/// {
///     OrderId = "123",
///     Amount = 99.99m
/// });
/// </code>
/// </example>
public interface IInngestEventData
{
    /// <summary>
    /// The event name used when sending this event to Inngest.
    /// This should follow the convention of "domain/event.action" (e.g., "shop/order.created").
    /// </summary>
    static abstract string EventName { get; }
}
