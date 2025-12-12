namespace Inngest;

/// <summary>
/// Interface for Inngest function handlers.
/// Implement this interface to create attribute-based Inngest functions with dependency injection support.
/// </summary>
/// <example>
/// <code>
/// [InngestFunction("order-processor", Name = "Process Order")]
/// [EventTrigger("shop/order.created")]
/// [Retry(Attempts = 3)]
/// public class OrderProcessor : IInngestFunction
/// {
///     private readonly IOrderService _orderService;
///
///     public OrderProcessor(IOrderService orderService)
///     {
///         _orderService = orderService;
///     }
///
///     public async Task&lt;object?&gt; ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
///     {
///         var order = await context.Step.Run("validate", () =&gt; _orderService.ValidateOrder());
///         return new { success = true, orderId = order.Id };
///     }
/// }
/// </code>
/// </example>
public interface IInngestFunction
{
    /// <summary>
    /// Executes the function logic
    /// </summary>
    /// <param name="context">The Inngest execution context containing event data and step tools</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The function result, or null</returns>
    Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for Inngest function handlers with strongly-typed event data.
/// Implement this interface when you want type-safe access to event data.
/// </summary>
/// <typeparam name="TEventData">The type of the event data payload</typeparam>
/// <example>
/// <code>
/// public class OrderCreatedEvent
/// {
///     public string OrderId { get; set; }
///     public decimal Amount { get; set; }
/// }
///
/// [InngestFunction("order-processor")]
/// [EventTrigger("shop/order.created")]
/// public class OrderProcessor : IInngestFunction&lt;OrderCreatedEvent&gt;
/// {
///     public async Task&lt;object?&gt; ExecuteAsync(InngestContext&lt;OrderCreatedEvent&gt; context, CancellationToken ct)
///     {
///         // Strongly typed access to event data
///         var orderId = context.Event.Data.OrderId;
///         var amount = context.Event.Data.Amount;
///
///         return new { processed = true };
///     }
/// }
/// </code>
/// </example>
public interface IInngestFunction<TEventData> where TEventData : class
{
    /// <summary>
    /// Executes the function logic with strongly-typed event data
    /// </summary>
    /// <param name="context">The typed Inngest execution context containing event data and step tools</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The function result, or null</returns>
    Task<object?> ExecuteAsync(InngestContext<TEventData> context, CancellationToken cancellationToken = default);
}
