using System.Text.Json;
using Inngest;
using Inngest.Attributes;
using Inngest.Steps;

namespace InngestExample.Functions;

/// <summary>
/// Example payment processing function demonstrating flow control features
/// critical for production payment handling.
///
/// This function demonstrates:
/// - [Throttle]: QUEUES events at 20/minute (unlike RateLimit which DROPS events)
/// - [Concurrency]: Serializes processing per payment ID to prevent duplicate donors
/// - [Idempotency]: Prevents duplicate processing when webhooks retry
/// - [Timeout]: Cancels hanging requests to prevent queue buildup
/// </summary>
[InngestFunction("payment-processor", Name = "Process Payment Webhook")]
[EventTrigger("payment/received")]
[Throttle(20, "1m", Key = "event.data.customerId")]  // Queue, don't drop
[Concurrency(1, Key = "event.data.paymentId")]       // Serialize per payment
[Idempotency("event.data.paymentId")]                // One execution per payment
[Timeout(Finish = "30s")]                            // Cancel if hanging
public class PaymentProcessorFunction : IInngestFunction
{
    private readonly ILogger<PaymentProcessorFunction> _logger;

    public PaymentProcessorFunction(ILogger<PaymentProcessorFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
    {
        // Extract payment data from event (Data is JsonElement when deserialized)
        var data = (JsonElement?)context.Event.Data;
        var paymentId = data?.GetProperty("paymentId").GetString() ?? "unknown";
        var customerId = data?.GetProperty("customerId").GetString() ?? "unknown";
        var amount = data?.GetProperty("amount").GetDecimal() ?? 0m;

        _logger.LogInformation("Processing payment {PaymentId} for customer {CustomerId}, amount: {Amount}",
            paymentId, customerId, amount);

        // Step 1: Find or create donor (serialized by payment ID to prevent race conditions)
        var donor = await context.Step.Run("find-or-create-donor", async () =>
        {
            _logger.LogInformation("Finding or creating donor for customer {CustomerId}", customerId);
            await Task.Delay(100, cancellationToken);
            return new { donorId = $"donor_{customerId}", isNew = false };
        });

        // Step 2: Create contribution record
        var contribution = await context.Step.Run("create-contribution", async () =>
        {
            _logger.LogInformation("Creating contribution for payment {PaymentId}", paymentId);
            await Task.Delay(100, cancellationToken);
            return new
            {
                contributionId = $"contrib_{paymentId}",
                donorId = donor.donorId,
                amount = amount
            };
        });

        // Step 3: Emit event for downstream handlers (receipts, notifications)
        var eventIds = await context.Step.SendEvent("emit-contribution-created",
            new InngestEvent
            {
                Name = "contribution/created",
                Data = new
                {
                    contributionId = contribution.contributionId,
                    donorId = contribution.donorId,
                    customerId = customerId,
                    amount = amount
                }
            });

        _logger.LogInformation("Payment {PaymentId} processed, contribution {ContributionId} created, emitted {EventCount} events",
            paymentId, contribution.contributionId, eventIds.Length);

        return new
        {
            status = "completed",
            paymentId = paymentId,
            contributionId = contribution.contributionId,
            donorId = donor.donorId,
            eventsEmitted = eventIds
        };
    }
}

/// <summary>
/// Example receipt sender demonstrating idempotency for email delivery.
///
/// The [Idempotency] attribute ensures only one receipt email is sent per contribution,
/// even if the event is delivered multiple times due to retries.
/// </summary>
[InngestFunction("send-receipt", Name = "Send Donation Receipt")]
[EventTrigger("contribution/created")]
[Idempotency("event.data.contributionId")]  // One receipt per contribution
[Timeout(Finish = "15s")]                    // Emails should be quick
public class SendReceiptFunction : IInngestFunction
{
    private readonly ILogger<SendReceiptFunction> _logger;

    public SendReceiptFunction(ILogger<SendReceiptFunction> logger)
    {
        _logger = logger;
    }

    public async Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
    {
        var data = (JsonElement?)context.Event.Data;
        var contributionId = data?.GetProperty("contributionId").GetString() ?? "unknown";
        var donorId = data?.GetProperty("donorId").GetString() ?? "unknown";

        _logger.LogInformation("Sending receipt for contribution {ContributionId} to donor {DonorId}",
            contributionId, donorId);

        await context.Step.Run("send-email", async () =>
        {
            // Simulate email sending
            await Task.Delay(50, cancellationToken);
            _logger.LogInformation("Receipt email sent for contribution {ContributionId}", contributionId);
            return new { sent = true };
        });

        return new { status = "sent", contributionId = contributionId };
    }
}
