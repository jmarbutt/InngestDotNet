# Inngest.NET

A .NET SDK for [Inngest](https://www.inngest.com/), a platform for building reliable, scalable event-driven architectures.

## Installation

```bash
dotnet add package Inngest.NET
```

## Quick Start

```csharp
using Inngest;

// Configure in Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Inngest
builder.Services.AddInngest(
    eventKey: "your-event-key", 
    signingKey: "your-signing-key"
);

var app = builder.Build();

// Get the client from DI
var inngestClient = app.Services.GetRequiredService<IInngestClient>();

// Create a function that responds to events
inngestClient.CreateFunction("hello-world-handler", async (context) =>
{
    var result = await context.Step("process", async () =>
    {
        var eventData = context.Event.Data;
        // Your business logic here
        return new { message = "Hello from Inngest.NET!" };
    });
    
    return result;
});

// Set up endpoint for Inngest to call your functions
app.UseInngest("/api/inngest");

app.Run();
```

## Environment Variables

The SDK supports the following environment variables as specified by the Inngest SDK specification:

### Critical Variables

- `INNGEST_EVENT_KEY` - Your Inngest event key for sending events
- `INNGEST_SIGNING_KEY` - Your Inngest signing key for authentication
- `INNGEST_SIGNING_KEY_FALLBACK` - Optional fallback signing key
- `INNGEST_ENV` - The environment name to use when sending events

### Optional Variables

- `INNGEST_DEV` - Set to any non-empty value to use the Inngest Dev Server (or provide a URL like `http://localhost:8288`)
- `INNGEST_API_BASE_URL` - Override the Inngest API URL
- `INNGEST_EVENT_API_BASE_URL` - Override the Inngest Event API URL
- `INNGEST_SERVE_ORIGIN` - The origin (base URL) for your application that Inngest will call
- `INNGEST_SERVE_PATH` - The path for the Inngest endpoint in your application

When instantiating the client directly, you can omit parameters and they'll be read from environment variables:

```csharp
// This will use environment variables for configuration
var inngestClient = new InngestClient();
```

## Sending Events

```csharp
// Simple event with data
await inngestClient.SendEventAsync("user.signed_up", new { 
    id = 123,
    email = "user@example.com" 
});

// Custom event with user data and idempotency key
var evt = new InngestEvent("user.signed_up", new { 
    id = 123, 
    email = "user@example.com" 
})
.WithUser(new { id = "usr_123" })
.WithIdempotencyKey("unique-key-123");

await inngestClient.SendEventAsync(evt);

// Send multiple events in one request
var events = new List<InngestEvent> { 
    new InngestEvent("event.one", new { id = 1 }),
    new InngestEvent("event.two", new { id = 2 })
};

await inngestClient.SendEventsAsync(events);
```

## Creating Functions

### Basic Function

```csharp
inngestClient.CreateFunction("function-id", async (context) =>
{
    var result = await context.Step("step-id", async () =>
    {
        // Your business logic
        return new { success = true };
    });
    
    return result;
});
```

### Advanced Function Configuration

```csharp
// Event trigger
var eventTrigger = FunctionTrigger.CreateEventTrigger("user.created");

// Cron trigger (runs every hour)
var cronTrigger = FunctionTrigger.CreateCronTrigger("0 * * * *");

// Function with options
inngestClient.CreateFunction(
    id: "advanced-function",
    name: "Advanced Function Example",
    triggers: new[] { eventTrigger, cronTrigger },
    handler: async (context) =>
    {
        // Your function code
        return new { status = "complete" };
    },
    options: new FunctionOptions
    {
        Concurrency = 10,
        Retry = new RetryOptions
        {
            Attempts = 3,
            Interval = 1000,  // ms
            Factor = 2.0      // exponential backoff
        }
    }
);
```

## Steps with Retry

```csharp
// Configure retry for a step
var stepOptions = new StepOptions
{
    Retry = new RetryOptions
    {
        Attempts = 3,
        Interval = 1000,   // Base interval in ms
        Factor = 2.0,      // Exponential backoff factor
        MaxInterval = 30000 // Maximum retry interval
    }
};

// Execute a step with retry
var result = await context.Step("api-call", async () =>
{
    // This code will automatically retry on exceptions
    var response = await httpClient.GetAsync("https://api.example.com/data");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<MyResponseType>();
}, stepOptions);
```

## Secrets

```csharp
// Register secrets when creating the client
var inngestClient = new InngestClient(
    eventKey: "your-event-key",
    signingKey: "your-signing-key"
);
inngestClient.AddSecret("API_KEY", "secret-value");

// Access secrets in your function
inngestClient.CreateFunction("example-with-secrets", async (context) =>
{
    var result = await context.Step("use-secret", async () =>
    {
        var apiKey = context.GetSecret("API_KEY");
        // Use the API key to make authenticated requests
        return new { success = true };
    });
    
    return result;
});
```

## Function Steps Registration

When creating functions with Inngest, you must register all steps that your function will use. This ensures that the Inngest server knows about your function's structure during sync.

```csharp
// Define a function with steps
inngestClient.CreateFunction("my-function", async (context) =>
{
    // Step 1: Log the event
    await context.Step("log-event", async () =>
    {
        Console.WriteLine("Processing event...");
        return true;
    });

    // Step 2: Sleep for a moment
    await context.Sleep("wait-a-bit", TimeSpan.FromSeconds(1));

    // Step 3: Process data with retry options
    var result = await context.Step("process-data", async () =>
    {
        // Your logic here
        return new { success = true };
    }, new StepOptions
    {
        Retry = new RetryOptions { Attempts = 3 }
    });

    return result;
})
// Register the steps so Inngest knows about them during sync
.WithStep("log-event", "Log the event data") 
.WithSleep("wait-a-bit", 1)
.WithStep("process-data", "Process data with retries", new RetryOptions
{
    Attempts = 3
});
```

If steps are not registered, Inngest will throw errors like "Function has no steps" during sync.

## Running Inngest Dev Server

```
npx inngest-cli@latest dev --no-discovery
```

## License

MIT


