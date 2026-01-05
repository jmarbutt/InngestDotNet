namespace Inngest.Attributes;

/// <summary>
/// Configures timeouts for an Inngest function to automatically cancel
/// runs that take too long to start or finish.
/// </summary>
/// <remarks>
/// <para>
/// Use timeouts to prevent queue buildup and ensure functions don't hang indefinitely.
/// This is especially important when using concurrency controls, as duplicate webhooks
/// queue up and wait - if a request hangs, the queue grows unbounded.
/// </para>
/// <para>
/// There are two timeout types:
/// <list type="bullet">
/// <item>
/// <term>Start</term>
/// <description>
/// Maximum time a run can wait in the queue before starting.
/// Runs exceeding this timeout are cancelled before executing.
/// </description>
/// </item>
/// <item>
/// <term>Finish</term>
/// <description>
/// Maximum time a run can execute after starting.
/// Runs exceeding this timeout are cancelled during execution.
/// </description>
/// </item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Cancel if function takes longer than 30 seconds to complete
/// [InngestFunction("create-contribution")]
/// [EventTrigger("payment/received")]
/// [Timeout(Finish = "30s")]
/// public class CreateContributionFunction : IInngestFunction { }
///
/// // Cancel if queued longer than 1 minute or runs longer than 2 minutes
/// [InngestFunction("process-order")]
/// [EventTrigger("order/created")]
/// [Timeout(Start = "1m", Finish = "2m")]
/// public class ProcessOrderFunction : IInngestFunction { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TimeoutAttribute : Attribute
{
    /// <summary>
    /// Maximum time a run can wait in the queue before starting.
    /// Uses Inngest time string format (e.g., "10s", "1m", "1h").
    /// If exceeded, the run is cancelled before it starts.
    /// </summary>
    public string? Start { get; set; }

    /// <summary>
    /// Maximum time a run can execute after starting.
    /// Uses Inngest time string format (e.g., "30s", "5m", "1h").
    /// If exceeded, the run is cancelled during execution.
    /// </summary>
    public string? Finish { get; set; }

    /// <summary>
    /// Creates a new timeout attribute.
    /// At least one of Start or Finish must be specified.
    /// </summary>
    public TimeoutAttribute()
    {
    }
}
