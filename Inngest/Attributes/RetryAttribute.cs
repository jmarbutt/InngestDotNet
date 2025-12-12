namespace Inngest.Attributes;

/// <summary>
/// Configures retry behavior for an Inngest function.
/// </summary>
/// <example>
/// <code>
/// [InngestFunction("order-processor")]
/// [EventTrigger("shop/order.created")]
/// [Retry(Attempts = 5)]
/// public class OrderProcessor : IInngestFunction
/// {
///     // Function will retry up to 5 times on failure
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RetryAttribute : Attribute
{
    /// <summary>
    /// The maximum number of retry attempts.
    /// Default is 3 if not specified.
    /// </summary>
    public int Attempts { get; set; } = 3;
}
