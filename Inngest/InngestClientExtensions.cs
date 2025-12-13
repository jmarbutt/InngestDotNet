using System.Reflection;
using Inngest.Attributes;

namespace Inngest;

/// <summary>
/// Extension methods for <see cref="IInngestClient"/> providing strongly-typed event sending.
/// </summary>
public static class InngestClientExtensions
{
    /// <summary>
    /// Send a strongly-typed event to Inngest.
    /// The event name is derived from the <see cref="IInngestEventData.EventName"/> static property.
    /// </summary>
    /// <typeparam name="TEvent">The event type implementing <see cref="IInngestEventData"/></typeparam>
    /// <param name="client">The Inngest client</param>
    /// <param name="eventData">The event data to send</param>
    /// <returns>True if the event was sent successfully</returns>
    /// <example>
    /// <code>
    /// public record OrderCreatedEvent : IInngestEventData
    /// {
    ///     public static string EventName => "shop/order.created";
    ///     public required string OrderId { get; init; }
    /// }
    ///
    /// await client.SendAsync(new OrderCreatedEvent { OrderId = "123" });
    /// </code>
    /// </example>
    public static Task<bool> SendAsync<TEvent>(this IInngestClient client, TEvent eventData)
        where TEvent : IInngestEventData
    {
        return client.SendEventAsync(TEvent.EventName, eventData);
    }

    /// <summary>
    /// Send a strongly-typed event to Inngest with additional event configuration.
    /// </summary>
    /// <typeparam name="TEvent">The event type implementing <see cref="IInngestEventData"/></typeparam>
    /// <param name="client">The Inngest client</param>
    /// <param name="eventData">The event data to send</param>
    /// <param name="configure">Action to configure the event (e.g., set idempotency key, user data)</param>
    /// <returns>True if the event was sent successfully</returns>
    /// <example>
    /// <code>
    /// await client.SendAsync(new OrderCreatedEvent { OrderId = "123" }, evt =>
    /// {
    ///     evt.WithIdempotencyKey($"order-{eventData.OrderId}");
    ///     evt.WithUser(new { id = "user-456" });
    /// });
    /// </code>
    /// </example>
    public static Task<bool> SendAsync<TEvent>(this IInngestClient client, TEvent eventData, Action<InngestEvent> configure)
        where TEvent : IInngestEventData
    {
        var evt = new InngestEvent(TEvent.EventName, eventData);
        configure(evt);
        return client.SendEventAsync(evt);
    }

    /// <summary>
    /// Send multiple strongly-typed events to Inngest in a single request.
    /// All events must be of the same type.
    /// </summary>
    /// <typeparam name="TEvent">The event type implementing <see cref="IInngestEventData"/></typeparam>
    /// <param name="client">The Inngest client</param>
    /// <param name="events">The events to send</param>
    /// <returns>True if the events were sent successfully</returns>
    public static Task<bool> SendManyAsync<TEvent>(this IInngestClient client, IEnumerable<TEvent> events)
        where TEvent : IInngestEventData
    {
        var inngestEvents = events.Select(e => new InngestEvent(TEvent.EventName, e));
        return client.SendEventsAsync(inngestEvents);
    }

    /// <summary>
    /// Send a strongly-typed event to Inngest using attribute-based event name discovery.
    /// The event type must be decorated with <see cref="InngestEventAttribute"/>.
    /// </summary>
    /// <typeparam name="TEvent">The event type decorated with <see cref="InngestEventAttribute"/></typeparam>
    /// <param name="client">The Inngest client</param>
    /// <param name="eventData">The event data to send</param>
    /// <returns>True if the event was sent successfully</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the event type is not decorated with <see cref="InngestEventAttribute"/>
    /// </exception>
    /// <example>
    /// <code>
    /// [InngestEvent("shop/order.created")]
    /// public record OrderCreatedEvent
    /// {
    ///     public required string OrderId { get; init; }
    /// }
    ///
    /// await client.SendByAttributeAsync(new OrderCreatedEvent { OrderId = "123" });
    /// </code>
    /// </example>
    public static Task<bool> SendByAttributeAsync<TEvent>(this IInngestClient client, TEvent eventData)
        where TEvent : class
    {
        var eventName = GetEventNameFromAttribute<TEvent>();
        return client.SendEventAsync(eventName, eventData);
    }

    /// <summary>
    /// Send multiple strongly-typed events to Inngest using attribute-based event name discovery.
    /// </summary>
    /// <typeparam name="TEvent">The event type decorated with <see cref="InngestEventAttribute"/></typeparam>
    /// <param name="client">The Inngest client</param>
    /// <param name="events">The events to send</param>
    /// <returns>True if the events were sent successfully</returns>
    public static Task<bool> SendManyByAttributeAsync<TEvent>(this IInngestClient client, IEnumerable<TEvent> events)
        where TEvent : class
    {
        var eventName = GetEventNameFromAttribute<TEvent>();
        var inngestEvents = events.Select(e => new InngestEvent(eventName, e));
        return client.SendEventsAsync(inngestEvents);
    }

    /// <summary>
    /// Gets the event name from a type, checking both <see cref="IInngestEventData"/> implementation
    /// and <see cref="InngestEventAttribute"/> decoration.
    /// </summary>
    /// <typeparam name="TEvent">The event type</typeparam>
    /// <returns>The event name</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the type doesn't implement <see cref="IInngestEventData"/> or have <see cref="InngestEventAttribute"/>
    /// </exception>
    public static string GetEventName<TEvent>()
    {
        var type = typeof(TEvent);

        // Check for IInngestEventData implementation first (compile-time)
        if (typeof(IInngestEventData).IsAssignableFrom(type))
        {
            // Get the static EventName property
            var property = type.GetProperty("EventName", BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                var value = property.GetValue(null) as string;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        // Fall back to attribute
        return GetEventNameFromAttribute<TEvent>();
    }

    private static string GetEventNameFromAttribute<TEvent>()
    {
        var attribute = typeof(TEvent).GetCustomAttribute<InngestEventAttribute>();
        if (attribute == null)
        {
            throw new InvalidOperationException(
                $"Type '{typeof(TEvent).Name}' must be decorated with [InngestEvent(\"event/name\")] attribute " +
                $"or implement IInngestEventData interface.");
        }
        return attribute.Name;
    }
}
