using Inngest;
using Inngest.Attributes;

namespace InngestExample.Functions;

/// <summary>
/// Example scheduled function that runs every 30 minutes
/// </summary>
[InngestFunction("scheduled-task", Name = "Scheduled Background Task")]
[CronTrigger("*/30 * * * *")]
[Concurrency(1)]
[Retry(Attempts = 3)]
public class ScheduledTaskFunction : IInngestFunction
{
    private readonly ILogger<ScheduledTaskFunction> _logger;

    public ScheduledTaskFunction(ILogger<ScheduledTaskFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
    {
        var report = await context.Step.Run("generate-report", async () =>
        {
            _logger.LogInformation("Running scheduled task at {Time}", DateTime.UtcNow);
            await Task.Delay(50, cancellationToken);
            return new { generated = DateTime.UtcNow, items = 42 };
        });

        await context.Step.Run("send-notification", async () =>
        {
            _logger.LogInformation("Report generated with {Items} items", report.items);
            await Task.Delay(10, cancellationToken);
            return true;
        });

        return new { status = "success", report };
    }
}
