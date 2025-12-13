using Inngest;
using InngestExample.Functions;
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

// Trigger an event
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
