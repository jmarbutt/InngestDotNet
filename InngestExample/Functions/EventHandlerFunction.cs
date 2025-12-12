using System.Text.Json;
using Inngest;
using Inngest.Attributes;

namespace InngestExample.Functions;

/// <summary>
/// Example function that handles a custom event using the attribute-based pattern
/// </summary>
[InngestFunction("my-event-handler", Name = "My Event Handler")]
[EventTrigger("test/my.event")]
[Retry(Attempts = 3)]
public class EventHandlerFunction : IInngestFunction
{
    private readonly ILogger<EventHandlerFunction> _logger;

    public EventHandlerFunction(ILogger<EventHandlerFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
    {
        // Step 1: Log the event (memoized - only runs once)
        var logged = await context.Step.Run("log-event", async () =>
        {
            _logger.LogInformation("Received event with data: {Data}",
                JsonSerializer.Serialize(context.Event.Data));
            await Task.Delay(10, cancellationToken);
            return true;
        });

        // Step 2: Sleep for 5 seconds (durable - survives restarts)
        await context.Step.Sleep("wait-a-moment", TimeSpan.FromSeconds(5));

        // Step 3: Process the event
        var result = await context.Step.Run("process-event", async () =>
        {
            await Task.Delay(100, cancellationToken);
            return new
            {
                message = "Event processed successfully",
                timestamp = DateTime.UtcNow,
                runId = context.Run.RunId
            };
        });

        return result;
    }
}
