namespace Inngest.Attributes;

/// <summary>
/// Specifies the event name for a strongly-typed event data class.
/// Use this attribute as an alternative to implementing <see cref="IInngestEventData"/>
/// when you prefer attribute-based configuration or cannot use static abstract members.
/// </summary>
/// <remarks>
/// The event name should follow the convention of "domain/event.action" (e.g., "shop/order.created").
/// This attribute is discovered at runtime via reflection.
/// For compile-time type safety, prefer implementing <see cref="IInngestEventData"/> instead.
/// </remarks>
/// <example>
/// <code>
/// [InngestEvent("shop/order.created")]
/// public record OrderCreatedEvent
/// {
///     public required string OrderId { get; init; }
///     public required decimal Amount { get; init; }
/// }
///
/// // Usage
/// await inngestClient.SendAsync(new OrderCreatedEvent
/// {
///     OrderId = "123",
///     Amount = 99.99m
/// });
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class InngestEventAttribute : Attribute
{
    /// <summary>
    /// The event name used when sending this event to Inngest.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a new Inngest event attribute with the specified event name.
    /// </summary>
    /// <param name="name">The event name (e.g., "shop/order.created")</param>
    /// <exception cref="ArgumentException">Thrown when name is null or whitespace</exception>
    public InngestEventAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name cannot be null or empty", nameof(name));

        Name = name;
    }
}
