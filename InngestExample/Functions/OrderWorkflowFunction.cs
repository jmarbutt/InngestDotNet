using Inngest;
using Inngest.Attributes;
using Inngest.Steps;

namespace InngestExample.Functions;

/// <summary>
/// Event data for order created events
/// </summary>
public class OrderCreatedEvent
{
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? CustomerId { get; set; }
}

/// <summary>
/// Payment confirmation event data
/// </summary>
public class PaymentConfirmation
{
    public string? OrderId { get; set; }
    public string? TransactionId { get; set; }
    public decimal Amount { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// Example multi-step workflow demonstrating durable execution with strongly-typed events
/// </summary>
[InngestFunction("order-workflow", Name = "Process Order Workflow")]
[EventTrigger("shop/order.created")]
[Retry(Attempts = 5)]
public class OrderWorkflowFunction : IInngestFunction<OrderCreatedEvent>
{
    private readonly ILogger<OrderWorkflowFunction> _logger;

    public OrderWorkflowFunction(ILogger<OrderWorkflowFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        // Access strongly-typed event data
        var eventData = context.Event.Data;
        _logger.LogInformation("Processing order {OrderId} for customer {CustomerId}",
            eventData?.OrderId, eventData?.CustomerId);

        // Step 1: Validate order
        var order = await context.Step.Run("validate-order", () =>
        {
            var orderId = eventData?.OrderId ?? Guid.NewGuid().ToString();
            return new { orderId, status = "validated", amount = eventData?.Amount ?? 99.99m };
        });

        // Step 2: Reserve inventory
        var inventory = await context.Step.Run("reserve-inventory", async () =>
        {
            await Task.Delay(100, cancellationToken);
            return new { reserved = true, sku = "PROD-001" };
        });

        // Step 3: Process payment
        var payment = await context.Step.Run("process-payment", async () =>
        {
            await Task.Delay(200, cancellationToken);
            return new { transactionId = Guid.NewGuid().ToString(), success = true };
        });

        // Step 4: Wait for payment webhook (with timeout)
        var confirmation = await context.Step.WaitForEvent<PaymentConfirmation>(
            "wait-payment-confirmation",
            new WaitForEventOptions
            {
                Event = "stripe/payment.succeeded",
                Timeout = "1h",
                Match = "async.data.orderId == event.data.orderId"
            });

        if (confirmation == null)
        {
            // Payment confirmation timed out
            await context.Step.Run("cancel-order", async () =>
            {
                _logger.LogWarning("Payment confirmation timeout for order {OrderId}", order.orderId);
                await Task.Delay(50, cancellationToken);
                return new { cancelled = true, reason = "Payment confirmation timeout" };
            });

            return new { status = "cancelled", reason = "Payment timeout" };
        }

        // Step 5: Send confirmation email
        await context.Step.Run("send-confirmation-email", async () =>
        {
            _logger.LogInformation("Sending confirmation email for order {OrderId}", order.orderId);
            await Task.Delay(50, cancellationToken);
            return true;
        });

        return new
        {
            status = "completed",
            orderId = order.orderId,
            transactionId = payment.transactionId
        };
    }
}
