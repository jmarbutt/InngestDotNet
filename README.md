# Inngest.NET

A .NET SDK for [Inngest](https://www.inngest.com/), a platform for building reliable, scalable event-driven workflows.

## Features

- **Durable execution**: Steps automatically retry and resume from failures
- **Step primitives**: Run, Sleep, SleepUntil, WaitForEvent, Invoke, SendEvent
- **Flow control**: Concurrency, rate limiting, throttling, debounce, batching
- **Full observability**: Built-in logging and tracing support

## Installation

```bash
dotnet add package Inngest.NET
```

## Quick Start

```csharp
using Inngest;

var builder = WebApplication.CreateBuilder(args);

// Add Inngest with DI
builder.Services.AddInngest(options =>
{
    options.AppId = "my-app";
    // Keys can also be set via INNGEST_EVENT_KEY and INNGEST_SIGNING_KEY env vars
});

var app = builder.Build();

var inngest = app.Services.GetRequiredService<IInngestClient>();

// Create a function that responds to events
inngest.CreateFunction(
    id: "process-order",
    name: "Process Order",
    triggers: new[] { FunctionTrigger.CreateEventTrigger("shop/order.created") },
    handler: async (ctx) =>
    {
        // Steps are durable - they retry on failure and skip on replay
        var validated = await ctx.Step.Run("validate-order", async () =>
        {
            // Your business logic here
            return new { orderId = ctx.Event.Data.GetProperty("orderId").GetString() };
        });

        // Sleep for 5 minutes (durable - survives restarts)
        await ctx.Step.Sleep("wait-for-processing", TimeSpan.FromMinutes(5));

        // Wait for a payment confirmation event
        var payment = await ctx.Step.WaitForEvent<PaymentConfirmation>(
            "wait-for-payment",
            new WaitForEventOptions
            {
                Event = "stripe/payment.succeeded",
                Timeout = "1h",
                Match = "async.data.orderId == event.data.orderId"
            });

        return new { status = "completed", orderId = validated.orderId };
    });

// Set up endpoint for Inngest to call your functions
app.UseInngest("/api/inngest");

app.Run();
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
// Simple event
await inngest.SendEventAsync("user.signed_up", new {
    userId = "usr_123",
    email = "user@example.com"
});

// Event with metadata
var evt = new InngestEvent("user.signed_up", new { userId = "usr_123" })
    .WithUser(new { id = "usr_123" })
    .WithIdempotencyKey("signup-usr_123");

await inngest.SendEventAsync(evt);

// Batch events
await inngest.SendEventsAsync(new[] {
    new InngestEvent("order.created", new { orderId = "ord_1" }),
    new InngestEvent("order.created", new { orderId = "ord_2" })
});
```

## Step Primitives

### step.Run - Execute code with automatic retry

```csharp
var result = await ctx.Step.Run("fetch-user", async () =>
{
    var user = await userService.GetUserAsync(userId);
    return user;
});

// Synchronous version
var value = await ctx.Step.Run("compute", () => ComputeValue());
```

### step.Sleep - Pause execution

```csharp
// Sleep for a duration
await ctx.Step.Sleep("wait", TimeSpan.FromHours(1));

// Sleep using Inngest time string format
await ctx.Step.Sleep("wait", "30m");  // 30 minutes
await ctx.Step.Sleep("wait", "2h30m"); // 2.5 hours
```

### step.SleepUntil - Sleep until a specific time

```csharp
var targetTime = DateTimeOffset.UtcNow.AddDays(1);
await ctx.Step.SleepUntil("wait-until-tomorrow", targetTime);
```

### step.WaitForEvent - Wait for another event

```csharp
var payment = await ctx.Step.WaitForEvent<PaymentData>(
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
    await ctx.Step.Run("handle-timeout", () => CancelOrder());
}
```

### step.Invoke - Call another Inngest function

```csharp
var result = await ctx.Step.Invoke<ProcessResult>(
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
await ctx.Step.SendEvent("notify-team", new InngestEvent(
    "notification/send",
    new { message = "Order completed!", channel = "slack" }
));
```

## Function Configuration

### Concurrency

```csharp
inngest.CreateFunction(
    id: "limited-function",
    triggers: new[] { FunctionTrigger.CreateEventTrigger("task/created") },
    handler: async (ctx) => { /* ... */ },
    options: new FunctionOptions
    {
        // Simple concurrency limit
        Concurrency = 5,

        // Or advanced with key-based limits
        ConcurrencyOptions = new ConcurrencyOptions
        {
            Limit = 1,
            Key = "event.data.userId",  // One concurrent execution per user
            Scope = "fn"  // "fn" or "env"
        }
    });
```

### Rate Limiting

```csharp
options: new FunctionOptions
{
    RateLimit = new RateLimitOptions
    {
        Limit = 100,
        Period = "1h",
        Key = "event.data.customerId"  // Per-customer rate limit
    }
}
```

### Throttling

```csharp
options: new FunctionOptions
{
    Throttle = new ThrottleOptions
    {
        Limit = 10,
        Period = "1m",
        Key = "event.data.apiKey",
        Burst = 5  // Allow burst of 5 before throttling
    }
}
```

### Debouncing

```csharp
options: new FunctionOptions
{
    Debounce = new DebounceOptions
    {
        Period = "5s",
        Key = "event.data.userId",
        Timeout = "1m"  // Max wait time
    }
}
```

### Event Batching

```csharp
options: new FunctionOptions
{
    Batch = new BatchOptions
    {
        MaxSize = 100,
        Timeout = "10s",
        Key = "event.data.tenantId"
    }
}
```

### Cancellation

```csharp
options: new FunctionOptions
{
    Cancellation = new CancellationOptions
    {
        Event = "order/cancelled",
        Match = "async.data.orderId == event.data.orderId"
    }
}
```

### Retries

```csharp
options: new FunctionOptions
{
    Retry = new RetryOptions
    {
        Attempts = 5,
        Interval = 1000,    // Base interval in ms
        Factor = 2.0,       // Exponential backoff
        MaxInterval = 60000 // Max 1 minute between retries
    }
}
```

### Idempotency

```csharp
options: new FunctionOptions
{
    IdempotencyKey = "event.data.transactionId"
}
```

## Error Handling

### Non-Retriable Errors

```csharp
await ctx.Step.Run("validate", () =>
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
await ctx.Step.Run("call-api", async () =>
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

## Cron Triggers

```csharp
inngest.CreateFunction(
    id: "daily-cleanup",
    name: "Daily Cleanup",
    triggers: new[] { FunctionTrigger.CreateCronTrigger("0 0 * * *") }, // Every day at midnight
    handler: async (ctx) =>
    {
        await ctx.Step.Run("cleanup", () => CleanupOldRecords());
        return new { cleaned = true };
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
dotnet run --project InngestExample
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
dotnet run --project InngestExample
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
