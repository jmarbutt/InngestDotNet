namespace Inngest.Attributes;

/// <summary>
/// Specifies a cron schedule trigger for an Inngest function.
/// Multiple cron triggers can be applied to a single function.
/// </summary>
/// <example>
/// <code>
/// [InngestFunction("daily-cleanup")]
/// [CronTrigger("0 0 * * *")] // Every day at midnight
/// public class DailyCleanup : IInngestFunction
/// {
///     public async Task&lt;object?&gt; ExecuteAsync(InngestContext context, CancellationToken ct)
///     {
///         // Cleanup logic
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CronTriggerAttribute : Attribute
{
    /// <summary>
    /// The cron expression for the schedule.
    /// Uses standard cron syntax (minute hour day-of-month month day-of-week).
    /// </summary>
    public string Cron { get; }

    /// <summary>
    /// Creates a new cron trigger attribute
    /// </summary>
    /// <param name="cronExpression">The cron expression (e.g., "0 0 * * *" for daily at midnight)</param>
    public CronTriggerAttribute(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("Cron expression cannot be null or empty", nameof(cronExpression));

        Cron = cronExpression;
    }
}
