namespace Inngest.Attributes;

/// <summary>
/// Specifies an event trigger for an Inngest function.
/// Multiple event triggers can be applied to a single function.
/// </summary>
/// <example>
/// <code>
/// [InngestFunction("multi-trigger")]
/// [EventTrigger("user/created")]
/// [EventTrigger("user/updated")]
/// public class UserHandler : IInngestFunction
/// {
///     public async Task&lt;object?&gt; ExecuteAsync(InngestContext context, CancellationToken ct)
///     {
///         var eventName = context.Event.Name;
///         // Handle both user/created and user/updated events
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EventTriggerAttribute : Attribute
{
    /// <summary>
    /// The event name that triggers this function
    /// </summary>
    public string Event { get; }

    /// <summary>
    /// Optional CEL expression to filter events.
    /// Only events matching the expression will trigger the function.
    /// </summary>
    /// <example>
    /// <code>
    /// [EventTrigger("order/created", Expression = "event.data.amount > 100")]
    /// </code>
    /// </example>
    public string? Expression { get; set; }

    /// <summary>
    /// Creates a new event trigger attribute
    /// </summary>
    /// <param name="eventName">The event name that triggers this function</param>
    public EventTriggerAttribute(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or empty", nameof(eventName));

        Event = eventName;
    }
}
