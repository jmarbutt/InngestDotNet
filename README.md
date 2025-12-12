# Inngest.NET

A .NET SDK for [Inngest](https://www.inngest.com/), a platform for building reliable, scalable event-driven workflows.

## Features

- **Attribute-based functions**: Define functions using familiar .NET patterns with attributes
- **Full dependency injection**: Constructor injection with scoped services per function invocation
- **Durable execution**: Steps automatically retry and resume from failures
- **Step primitives**: Run, Sleep, SleepUntil, WaitForEvent, Invoke, SendEvent
- **Flow control**: Concurrency, rate limiting, throttling, debounce, batching
- **Full observability**: Built-in logging with ILogger support

## Installation

```bash
dotnet add package Inngest.NET
```

## Quick Start

### 1. Configure Inngest in Program.cs

```csharp
using Inngest;

var builder = WebApplication.CreateBuilder(args);

// Add Inngest with configuration and auto-discover functions
builder.Services
    .AddInngest(options =>
    {
        options.AppId = "my-app";
        options.IsDev = true; // Use Inngest Dev Server
    })
    .AddFunctionsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Mount the Inngest endpoint
app.UseInngest("/api/inngest");

app.Run();
```

### 2. Create a Function

```csharp
using Inngest;
using Inngest.Attributes;

[InngestFunction("process-order", Name = "Process Order")]
[EventTrigger("shop/order.created")]
[Retry(Attempts = 5)]
public class OrderProcessor : IInngestFunction
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderProcessor> _logger;

    // Constructor injection - services are scoped per function invocation
    public OrderProcessor(IOrderService orderService, ILogger<OrderProcessor> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
    {
        // Step 1: Validate order (memoized - runs once, replays on retries)
        var order = await context.Step.Run("validate-order", async () =>
        {
            _logger.LogInformation("Validating order");
            return await _orderService.ValidateAsync(context.Event.Data);
        });

        // Step 2: Sleep for 5 minutes (durable - survives restarts)
        await context.Step.Sleep("wait-for-processing", TimeSpan.FromMinutes(5));

        // Step 3: Process payment
        var payment = await context.Step.Run("process-payment", async () =>
        {
            return await _orderService.ProcessPaymentAsync(order.Id);
        });

        return new { status = "completed", orderId = order.Id };
    }
}
```

### 3. Strongly-Typed Event Data

```csharp
public class OrderCreatedEvent
{
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? CustomerId { get; set; }
}

[InngestFunction("typed-order-handler", Name = "Typed Order Handler")]
[EventTrigger("shop/order.created")]
public class TypedOrderHandler : IInngestFunction<OrderCreatedEvent>
{
    public async Task<object?> ExecuteAsync(
        InngestContext<OrderCreatedEvent> context,
        CancellationToken cancellationToken)
    {
        // Access strongly-typed event data
        var eventData = context.Event.Data;

        await context.Step.Run("process", () =>
        {
            Console.WriteLine($"Order {eventData?.OrderId} for ${eventData?.Amount}");
            return true;
        });

        return new { processed = true };
    }
}
```

## Function Attributes

### InngestFunction

Marks a class as an Inngest function:

```csharp
[InngestFunction("my-function-id", Name = "Human Readable Name")]
```

### EventTrigger

Triggers the function when a specific event is received:

```csharp
[EventTrigger("user/signed.up")]
[EventTrigger("user/invited", Expression = "event.data.role == 'admin'")] // With filter
```

### CronTrigger

Triggers the function on a schedule:

```csharp
[CronTrigger("0 0 * * *")]  // Every day at midnight
[CronTrigger("*/30 * * * *")]  // Every 30 minutes
```

### Retry

Configures retry behavior:

```csharp
[Retry(Attempts = 5)]
```

### Concurrency

Limits concurrent executions:

```csharp
[Concurrency(5)]  // Max 5 concurrent executions
[Concurrency(1, Key = "event.data.userId")]  // Per-user concurrency
```

### RateLimit

Limits execution rate:

```csharp
[RateLimit(100, Period = "1h")]  // 100 per hour
[RateLimit(10, Period = "1m", Key = "event.data.customerId")]  // Per-customer
```

## Configuration

### Using Options Pattern

```csharp
// appsettings.json
{
  "Inngest": {
    "AppId": "my-app",
    "EventKey": "your-event-key",
    "SigningKey": "your-signing-key"
  }
}

// Program.cs
builder.Services
    .AddInngest(builder.Configuration.GetSection("Inngest"))
    .AddFunctionsFromAssembly(typeof(Program).Assembly);
```

### Using Action Configuration

```csharp
builder.Services
    .AddInngest(options =>
    {
        options.AppId = "my-app";
        options.EventKey = "your-event-key";
        options.SigningKey = "your-signing-key";
        options.IsDev = builder.Environment.IsDevelopment();
    })
    .AddFunction<OrderProcessor>()
    .AddFunction<EmailSender>();
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `INNGEST_EVENT_KEY` | Your Inngest event key for sending events |
| `INNGEST_SIGNING_KEY` | Your Inngest signing key for authentication |
| `INNGEST_SIGNING_KEY_FALLBACK` | Optional fallback signing key |
| `INNGEST_ENV` | Environment name (e.g., "production", "staging") |
| `INNGEST_DEV` | Set to any value or URL to use Inngest Dev Server |
| `INNGEST_SERVE_ORIGIN` | Base URL for your application |
| `INNGEST_SERVE_PATH` | Path for the Inngest endpoint |

## Sending Events

```csharp
// Inject IInngestClient where needed
public class OrderController : ControllerBase
{
    private readonly IInngestClient _inngest;

    public OrderController(IInngestClient inngest) => _inngest = inngest;

    [HttpPost]
    public async Task<IActionResult> CreateOrder(Order order)
    {
        // Simple event
        await _inngest.SendEventAsync("shop/order.created", new {
            orderId = order.Id,
            amount = order.Total
        });

        // Event with metadata
        var evt = new InngestEvent("shop/order.created", new { orderId = order.Id })
            .WithUser(new { id = order.CustomerId })
            .WithIdempotencyKey($"order-{order.Id}");

        await _inngest.SendEventAsync(evt);

        return Ok();
    }
}
```

## Step Primitives

### step.Run - Execute code with automatic retry

```csharp
var result = await context.Step.Run("fetch-user", async () =>
{
    var user = await userService.GetUserAsync(userId);
    return user;
});

// Synchronous version
var value = await context.Step.Run("compute", () => ComputeValue());
```

### step.Sleep - Pause execution

```csharp
// Sleep for a duration
await context.Step.Sleep("wait", TimeSpan.FromHours(1));

// Sleep using Inngest time string format
await context.Step.Sleep("wait", "30m");  // 30 minutes
await context.Step.Sleep("wait", "2h30m"); // 2.5 hours
```

### step.SleepUntil - Sleep until a specific time

```csharp
var targetTime = DateTimeOffset.UtcNow.AddDays(1);
await context.Step.SleepUntil("wait-until-tomorrow", targetTime);
```

### step.WaitForEvent - Wait for another event

```csharp
var payment = await context.Step.WaitForEvent<PaymentData>(
    "wait-payment",
    new WaitForEventOptions
    {
        Event = "payment/completed",
        Timeout = "24h",
        Match = "async.data.orderId == event.data.orderId"
    });

if (payment == null)
{
    // Timeout occurred
    await context.Step.Run("handle-timeout", () => CancelOrder());
}
```

### step.Invoke - Call another Inngest function

```csharp
var result = await context.Step.Invoke<ProcessResult>(
    "process-payment",
    new InvokeOptions
    {
        FunctionId = "my-app-payment-processor",
        Data = new { amount = 100, currency = "USD" },
        Timeout = "5m"
    });
```

### step.SendEvent - Emit events from within a function

```csharp
await context.Step.SendEvent("notify-team", new InngestEvent(
    "notification/send",
    new { message = "Order completed!", channel = "slack" }
));
```

## Error Handling

### Non-Retriable Errors

```csharp
await context.Step.Run("validate", () =>
{
    if (!IsValid(data))
    {
        // This error won't be retried
        throw new NonRetriableException("Invalid data format");
    }
    return data;
});
```

### Retry-After Errors

```csharp
await context.Step.Run("call-api", async () =>
{
    var response = await client.GetAsync(url);
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
    {
        // Retry after the specified time
        throw new RetryAfterException(TimeSpan.FromMinutes(5));
    }
    return await response.Content.ReadAsStringAsync();
});
```

## Running Locally

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- [Node.js](https://nodejs.org/) (for running the Inngest Dev Server via npx)

### Option A: Auto-Discovery with `-u` Flag (Recommended)

This approach tells the Dev Server where to find your app, enabling automatic function discovery.

**Terminal 1** - Start your .NET app first:
```bash
dotnet run --project YourProject
```

**Terminal 2** - Start the Dev Server pointing to your app:
```bash
npx inngest-cli@latest dev -u http://localhost:5000/api/inngest
```

The Dev Server will automatically discover and sync your functions.

### Option B: Manual Registration

If you prefer to start the Dev Server first:

**Terminal 1** - Start the Dev Server:
```bash
npx inngest-cli@latest dev --no-discovery
```

**Terminal 2** - Start your .NET app:
```bash
dotnet run --project YourProject
```

The `--no-discovery` flag prevents auto-discovery. Your app will register its functions when it starts.

> **Note:** Leave both terminals running during development.

### Access the Dev Server UI

Open your browser to [http://localhost:8288](http://localhost:8288) to:

- View all registered functions
- Send test events to trigger functions
- Monitor function executions in real-time
- Inspect step-by-step execution and retries
- Debug failures and view logs

### Environment Variables for Local Development

For local development, you typically don't need to set any environment variables. The SDK auto-detects the Dev Server.

To explicitly configure the Dev Server URL:

```bash
export INNGEST_DEV=http://localhost:8288
```

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Functions not appearing in Dev Server | Ensure your app is running and the `/api/inngest` endpoint is accessible |
| "Connection refused" errors | Check that the Dev Server is running on port 8288 |
| Events not triggering functions | Verify the event name matches your function's trigger exactly |

## License

MIT
