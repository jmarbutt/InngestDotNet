namespace Inngest.Attributes;

/// <summary>
/// Marks a class as an Inngest function handler.
/// The class must implement <see cref="IInngestFunction"/> or <see cref="IInngestFunction{TEventData}"/>.
/// </summary>
/// <example>
/// <code>
/// [InngestFunction("order-processor", Name = "Process Order")]
/// [EventTrigger("shop/order.created")]
/// public class OrderProcessor : IInngestFunction
/// {
///     public async Task&lt;object?&gt; ExecuteAsync(InngestContext context, CancellationToken ct)
///     {
///         // Function implementation
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InngestFunctionAttribute : Attribute
{
    /// <summary>
    /// The unique identifier for this function.
    /// This ID is combined with the app ID to form the full function ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Optional display name for the function.
    /// If not specified, defaults to the function ID.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Creates a new Inngest function attribute
    /// </summary>
    /// <param name="id">The unique identifier for this function</param>
    public InngestFunctionAttribute(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Function ID cannot be null or empty", nameof(id));

        Id = id;
    }
}
