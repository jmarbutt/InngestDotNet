using System.Text.Json;
using Inngest;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var inngestClient = new InngestClient("your-event-key", "your-signing-key", "http://127.0.0.1:8288");

// Inject the InngestClient into the middleware
builder.Services.AddSingleton(inngestClient);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();




// Register the function to handle "my-event"
inngestClient.CreateFunction("my-event-handler", async (context) =>
{
    // Step 1: Log the event
    await context.Step("log-event", async () =>
    {
        Console.WriteLine($"Received my-event with data: {JsonSerializer.Serialize(context.Event.Data)}");
        return true;
    });

    // Step 2: Sleep for 1 second
    await context.Sleep("wait-a-moment", TimeSpan.FromSeconds(1));

    // Step 3: Process the event
    var result = await context.Step("process-event", async () =>
    {
        // Example: Perform some operation based on the event data
        var eventData = context.Event.Data;
        // var someProperty = ((JsonElement)eventData).GetProperty("someProperty").GetString();

        return new { message = "my-event processed successfully" };
    });

    return result;
});

app.MapPost("/api/test_api/trigger-my-event", async ([FromBody] object eventData) =>
{
    // Send the "my-event" event
    var result = await inngestClient.SendEventAsync("my-event", eventData);
    return Results.Ok(result);
});




app.UseInngest("/api/inngest");

app.Run();

