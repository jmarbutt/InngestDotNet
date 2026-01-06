namespace Inngest.Attributes;

/// <summary>
/// Specifies an onFailure handler for an Inngest function.
/// The handler is invoked after all retries are exhausted for the parent function.
/// </summary>
/// <remarks>
/// OnFailure handlers are implemented as companion functions that trigger on
/// the <c>inngest/function.failed</c> event, filtered to the parent function's ID.
/// The handler receives the original event payload and error information.
/// </remarks>
/// <example>
/// <code>
/// // Function with an onFailure handler
/// [InngestFunction("process-payment")]
/// [EventTrigger("payment/created")]
/// [OnFailure(typeof(PaymentFailureHandler))]
/// public class ProcessPaymentFunction : IInngestFunction
/// {
///     public Task&lt;object?&gt; ExecuteAsync(InngestContext context, CancellationToken ct)
///     {
///         // Main function logic
///     }
/// }
///
/// // The failure handler class
/// public class PaymentFailureHandler : IInngestFailureHandler
/// {
///     private readonly ISentryService _sentry;
///     private readonly IPaymentService _payments;
///
///     public PaymentFailureHandler(ISentryService sentry, IPaymentService payments)
///     {
///         _sentry = sentry;
///         _payments = payments;
///     }
///
///     public async Task HandleFailureAsync(FailureContext context, CancellationToken ct)
///     {
///         // Capture to Sentry
///         _sentry.CaptureException(context.Failure.ToException());
///
///         // Mark payment as failed in database
///         var paymentId = context.OriginalEvent.Data.GetProperty("paymentId").GetString();
///         await _payments.MarkAsFailed(paymentId);
///
///         // Optionally send a failure notification event
///         await context.Step.SendEvent("notify-failure", new InngestEvent
///         {
///             Name = "payment/failed",
///             Data = new { paymentId, error = context.Failure.Error.Message }
///         });
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnFailureAttribute : Attribute
{
    /// <summary>
    /// The type of the failure handler class.
    /// Must implement <see cref="IInngestFailureHandler"/>.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// Creates a new OnFailure attribute
    /// </summary>
    /// <param name="handlerType">Type implementing IInngestFailureHandler</param>
    /// <exception cref="ArgumentException">If the type doesn't implement IInngestFailureHandler</exception>
    public OnFailureAttribute(Type handlerType)
    {
        if (!typeof(IInngestFailureHandler).IsAssignableFrom(handlerType))
        {
            throw new ArgumentException(
                $"Handler type {handlerType.Name} must implement IInngestFailureHandler",
                nameof(handlerType));
        }

        HandlerType = handlerType;
    }
}
