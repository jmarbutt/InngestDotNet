using System.Text.Json;
using Inngest;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Ensure dev mode is enabled for local development
Environment.SetEnvironmentVariable("INNGEST_DEV", "true");

// Create the Inngest client and add it to the service collection
// In a real application, get these keys from configuration
var inngestClient = new InngestClient(
    eventKey: "your-event-key",
    signingKey: "your-signing-key",
    // For local development with dev server
    apiOrigin: "http://127.0.0.1:8288",
    eventApiOrigin: "http://127.0.0.1:8288");

// Add a secret that will be accessible to the functions
inngestClient.AddSecret("API_KEY", "your-api-key-here");

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

// Define a function with a simple event trigger
inngestClient.CreateFunction("my-event-handler", async (context) =>
{
    // Step 1: Log the event
    await context.Step("log-event", async () =>
    {
        Console.WriteLine($"Received my-event with data: {JsonSerializer.Serialize(context.Event.Data)}");
        await Task.Delay(1); // Add await to avoid compiler warning
        return true;
    });

    // Step 2: Sleep for 1 second
    await context.Sleep("wait-a-moment", TimeSpan.FromSeconds(1));

    // Step 3: Process the event (with retry options)
    var stepOptions = new StepOptions
    {
        Retry = new RetryOptions
        {
            Attempts = 3,
            Interval = 1000,
            Factor = 2.0
        }
    };

    var result = await context.Step("process-event", async () =>
    {
        // Example: Access event data
        var eventData = context.Event.Data;
        
        // You can also access secrets
        var apiKey = context.GetSecret("API_KEY");
        
        await Task.Delay(1); // Add await to avoid compiler warning
        return new { message = "my-event processed successfully", timestamp = DateTime.UtcNow };
    }, stepOptions);

    return result;
})
// Register the steps so Inngest knows about them during sync
.WithStep("log-event", "Log the event data") 
.WithSleep("wait-a-moment", 1)
.WithStep("process-event", "Process the event data", new RetryOptions
{
    Attempts = 3,
    Interval = 1000,
    Factor = 2.0
});

// Register a function with cron trigger
var cronTrigger = FunctionTrigger.CreateCronTrigger("*/30 * * * *"); // Run every 30 minutes
inngestClient.CreateFunction(
    id: "scheduled-task",
    name: "Scheduled Background Task",
    triggers: new[] { cronTrigger },
    handler: async (context) =>
    {
        await context.Step("run-schedule", async () =>
        {
            Console.WriteLine($"Running scheduled task at {DateTime.UtcNow}");
            // Your scheduled task logic here
            await Task.Delay(1); // Add await to avoid compiler warning
            return new { completed = true, time = DateTime.UtcNow };
        });

        return new { status = "success" };
    },
    options: new FunctionOptions
    {
        Concurrency = 1,
        Retry = new RetryOptions
        {
            Attempts = 3
        }
    }
)
// Register the steps so Inngest knows about them during sync
.WithStep("run-schedule", "Run the scheduled task");

// Create an API endpoint to trigger events
app.MapPost("/api/trigger-event", async ([FromBody] EventRequest request) =>
{
    if (string.IsNullOrEmpty(request.EventName))
    {
        return Results.BadRequest("Event name is required");
    }

    // Create and send an event
    var evt = new InngestEvent(request.EventName, request.Data ?? new { });
    
    // Optionally add user information
    if (request.UserId != null)
    {
        evt.WithUser(new { id = request.UserId });
    }
    
    // Send the event
    var result = await inngestClient.SendEventAsync(evt);
    
    return Results.Ok(new { success = result });
});

// Route to handle Inngest webhooks
app.UseInngest("/api/inngest");

app.Run();

// Request model for the trigger endpoint
public class EventRequest
{
    public required string EventName { get; set; }
    public object? Data { get; set; }
    public string? UserId { get; set; }
}

