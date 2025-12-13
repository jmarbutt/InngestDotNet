using Inngest;
using InngestExample.Events;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Inngest
// Set INNGEST_DEV=true for local development, or INNGEST_DEV=false for production mode
// Set INNGEST_SIGNING_KEY and INNGEST_EVENT_KEY for production
builder.Services
    .AddInngest(options =>
    {
        options.AppId = "my-dotnet-app";
        // IsDev, SigningKey, EventKey, ApiOrigin, EventApiOrigin
        // are all read from environment variables if not set here
    })
    .AddFunctionsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// =============================================================================
// API Endpoints
// =============================================================================

// Trigger an event (legacy - with magic strings)
app.MapPost("/api/trigger-event", async ([FromServices] IInngestClient inngestClient, [FromBody] EventRequest request) =>
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

// Create an order (strongly-typed - no magic strings!)
app.MapPost("/api/orders", async ([FromServices] IInngestClient inngestClient, [FromBody] CreateOrderRequest request) =>
{
    // Create the strongly-typed event - the event name is defined in OrderCreatedEvent.EventName
    var orderEvent = new OrderCreatedEvent
    {
        OrderId = Guid.NewGuid().ToString(),
        Amount = request.Amount,
        CustomerId = request.CustomerId
    };

    // SendAsync<T> automatically uses OrderCreatedEvent.EventName ("shop/order.created")
    var result = await inngestClient.SendAsync(orderEvent);

    return Results.Ok(new
    {
        success = result,
        orderId = orderEvent.OrderId,
        eventName = OrderCreatedEvent.EventName // Shows the event name for debugging
    });
})
.WithName("CreateOrder")
.WithOpenApi();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
.WithName("HealthCheck")
.WithOpenApi();

// =============================================================================
// Step Error Testing Endpoints
// =============================================================================

// Test step error handling - demonstrates retry behavior
app.MapPost("/api/test-step-error", async (
    [FromServices] IInngestClient inngestClient,
    [FromBody] TestStepErrorRequest request) =>
{
    var evt = new InngestEvent("test/step-error", new
    {
        failAt = request.FailAt,
        nonRetriable = request.NonRetriable
    });

    var result = await inngestClient.SendEventAsync(evt);
    return Results.Ok(new
    {
        success = result,
        eventId = evt.Id,
        message = $"Step error test triggered - failAt: {request.FailAt}, nonRetriable: {request.NonRetriable}"
    });
})
.WithName("TestStepError")
.WithDescription("Test step error handling and retry behavior")
.WithOpenApi();

// Test transient errors that succeed after retries
app.MapPost("/api/test-transient-error", async (
    [FromServices] IInngestClient inngestClient,
    [FromBody] TestTransientErrorRequest? request) =>
{
    var evt = new InngestEvent("test/transient-error", new
    {
        message = request?.Message ?? "Testing transient failures"
    });

    var result = await inngestClient.SendEventAsync(evt);
    return Results.Ok(new
    {
        success = result,
        eventId = evt.Id,
        message = "Transient error test triggered - will fail twice then succeed"
    });
})
.WithName("TestTransientError")
.WithDescription("Test transient errors that succeed after retries")
.WithOpenApi();

// Test retry-after delays
app.MapPost("/api/test-retry-after", async (
    [FromServices] IInngestClient inngestClient,
    [FromBody] TestRetryAfterRequest? request) =>
{
    var evt = new InngestEvent("test/retry-after", new
    {
        delaySeconds = request?.DelaySeconds ?? 30
    });

    var result = await inngestClient.SendEventAsync(evt);
    return Results.Ok(new
    {
        success = result,
        eventId = evt.Id,
        message = $"Retry-after test triggered - will wait {request?.DelaySeconds ?? 30}s before retrying"
    });
})
.WithName("TestRetryAfter")
.WithDescription("Test retry-after delays for rate limiting scenarios")
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

public class CreateOrderRequest
{
    public required decimal Amount { get; set; }
    public required string CustomerId { get; set; }
}

public class TestStepErrorRequest
{
    /// <summary>
    /// Which step should fail: "first", "second", or "none"
    /// </summary>
    public string FailAt { get; set; } = "first";

    /// <summary>
    /// If true, the error is non-retriable
    /// </summary>
    public bool NonRetriable { get; set; } = false;
}

public class TestTransientErrorRequest
{
    public string? Message { get; set; }
}

public class TestRetryAfterRequest
{
    /// <summary>
    /// How many seconds to wait before retrying
    /// </summary>
    public int DelaySeconds { get; set; } = 30;
}
