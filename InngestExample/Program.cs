using System.Text.Json;
using Inngest;
using Inngest.Steps;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Ensure dev mode is enabled for local development
Environment.SetEnvironmentVariable("INNGEST_DEV", "true");

// Create the Inngest client
var inngestClient = new InngestClient(
    eventKey: "your-event-key",
    signingKey: "your-signing-key",
    apiOrigin: "http://127.0.0.1:8288",
    eventApiOrigin: "http://127.0.0.1:8288",
    appId: "my-dotnet-app");

// Register the Inngest client with DI
builder.Services.AddSingleton<IInngestClient>(inngestClient);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// =============================================================================
// Example 1: Simple event-triggered function with steps
// =============================================================================
inngestClient.CreateFunction("my-event-handler", async (ctx) =>
{
    // Step 1: Log the event (memoized - only runs once)
    var logged = await ctx.Step.Run("log-event", async () =>
    {
        Console.WriteLine($"Received my-event-handler with data: {JsonSerializer.Serialize(ctx.Event.Data)}");
        await Task.Delay(10); // Simulate some work
        return true;
    });

    // Step 2: Sleep for 5 seconds (durable - survives restarts)
    await ctx.Step.Sleep("wait-a-moment", TimeSpan.FromSeconds(5));

    // Step 3: Process the event with retry options
    var result = await ctx.Step.Run("process-event", async () =>
    {
        // Access event data
        var eventData = ctx.Event.Data;

        // Simulate processing
        await Task.Delay(100);

        return new
        {
            message = "Event processed successfully",
            timestamp = DateTime.UtcNow,
            runId = ctx.Run.RunId
        };
    }, new StepRunOptions
    {
        Name = "Process Event Data"
    });

    return result;
});

// =============================================================================
// Example 2: Scheduled function (cron trigger)
// =============================================================================
var cronTrigger = FunctionTrigger.CreateCronTrigger("*/30 * * * *"); // Run every 30 minutes
inngestClient.CreateFunction(
    id: "scheduled-task",
    name: "Scheduled Background Task",
    triggers: new[] { cronTrigger },
    handler: async (ctx) =>
    {
        var report = await ctx.Step.Run("generate-report", async () =>
        {
            Console.WriteLine($"Running scheduled task at {DateTime.UtcNow}");
            await Task.Delay(50);
            return new { generated = DateTime.UtcNow, items = 42 };
        });

        await ctx.Step.Run("send-notification", async () =>
        {
            Console.WriteLine($"Report generated with {report.items} items");
            await Task.Delay(10);
            return true;
        });

        return new { status = "success", report };
    },
    options: new FunctionOptions
    {
        Concurrency = 1,
        Retry = new RetryOptions { Attempts = 3 }
    }
);

// =============================================================================
// Example 3: Multi-step workflow demonstrating durable execution
// =============================================================================
inngestClient.CreateFunction(
    id: "order-workflow",
    name: "Process Order Workflow",
    triggers: new[] { FunctionTrigger.CreateEventTrigger("shop/order.created") },
    handler: async (ctx) =>
    {
        // Step 1: Validate order
        var order = await ctx.Step.Run("validate-order", () =>
        {
            // Synchronous step (no async needed)
            var orderId = Guid.NewGuid().ToString();
            return new { orderId, status = "validated", amount = 99.99 };
        });

        // Step 2: Reserve inventory (with retry)
        var inventory = await ctx.Step.Run("reserve-inventory", async () =>
        {
            await Task.Delay(100);
            return new { reserved = true, sku = "PROD-001" };
        });

        // Step 3: Process payment
        var payment = await ctx.Step.Run("process-payment", async () =>
        {
            await Task.Delay(200);
            return new { transactionId = Guid.NewGuid().ToString(), success = true };
        });

        // Step 4: Wait for payment webhook (with timeout)
        // Note: WaitForEvent will return null if timeout is reached
        var confirmation = await ctx.Step.WaitForEvent<PaymentConfirmation>(
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
            await ctx.Step.Run("cancel-order", async () =>
            {
                await Task.Delay(50);
                return new { cancelled = true, reason = "Payment confirmation timeout" };
            });

            return new { status = "cancelled", reason = "Payment timeout" };
        }

        // Step 5: Send confirmation email
        await ctx.Step.Run("send-confirmation-email", async () =>
        {
            Console.WriteLine($"Sending confirmation email for order {order.orderId}");
            await Task.Delay(50);
            return true;
        });

        return new
        {
            status = "completed",
            orderId = order.orderId,
            transactionId = payment.transactionId
        };
    },
    options: new FunctionOptions
    {
        Retry = new RetryOptions { Attempts = 5 }
    }
);

// =============================================================================
// API Endpoints
// =============================================================================

// Trigger an event
app.MapPost("/api/trigger-event", async ([FromBody] EventRequest request) =>
{
    if (string.IsNullOrEmpty(request.EventName))
    {
        return Results.BadRequest("Event name is required");
    }

    var evt = new InngestEvent(request.EventName, request.Data ?? new { });

    if (request.UserId != null)
    {
        evt.WithUser(new { id = request.UserId });
    }

    var result = await inngestClient.SendEventAsync(evt);

    return Results.Ok(new { success = result, eventId = evt.Id });
})
.WithName("TriggerEvent")
.WithOpenApi();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
.WithName("HealthCheck")
.WithOpenApi();

// Route to handle Inngest webhooks
app.UseInngest("/api/inngest");

app.Run();

// =============================================================================
// Request/Response Models
// =============================================================================

public class EventRequest
{
    public required string EventName { get; set; }
    public object? Data { get; set; }
    public string? UserId { get; set; }
}

public class PaymentConfirmation
{
    public string? OrderId { get; set; }
    public string? TransactionId { get; set; }
    public decimal Amount { get; set; }
    public bool Success { get; set; }
}
