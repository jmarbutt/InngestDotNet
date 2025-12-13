using System.Reflection;
using Inngest.Attributes;
using Inngest.Internal;

namespace Inngest.Tests;

/// <summary>
/// Tests for function registry and trigger derivation
/// </summary>
public class FunctionRegistryTests
{
    #region Test Function Definitions

    // Event with IInngestEventData
    public record TestOrderEvent : IInngestEventData
    {
        public static string EventName => "test/order.created";
        public string? OrderId { get; init; }
    }

    // Event with attribute
    [InngestEvent("test/payment.processed")]
    public record TestPaymentEvent
    {
        public string? PaymentId { get; init; }
    }

    // Function that should auto-derive trigger from event type
    [InngestFunction("auto-trigger-function", Name = "Auto Trigger Function")]
    public class AutoTriggerFunction : IInngestFunction<TestOrderEvent>
    {
        public Task<object?> ExecuteAsync(InngestContext<TestOrderEvent> context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    // Function with explicit trigger (should not auto-derive)
    [InngestFunction("explicit-trigger-function", Name = "Explicit Trigger Function")]
    [EventTrigger("custom/explicit.event")]
    public class ExplicitTriggerFunction : IInngestFunction<TestOrderEvent>
    {
        public Task<object?> ExecuteAsync(InngestContext<TestOrderEvent> context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    // Function with cron trigger
    [InngestFunction("cron-function", Name = "Cron Function")]
    [CronTrigger("0 * * * *")]
    public class CronFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { ran = true });
        }
    }

    // Function with multiple triggers
    [InngestFunction("multi-trigger-function", Name = "Multi Trigger Function")]
    [EventTrigger("event/one")]
    [EventTrigger("event/two")]
    public class MultiTriggerFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    // Function with retry and concurrency
    [InngestFunction("configured-function", Name = "Configured Function")]
    [EventTrigger("test/configured")]
    [Retry(Attempts = 5)]
    [Concurrency(10, Key = "event.data.userId")]
    [RateLimit(100, "1h")]
    public class ConfiguredFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    // Function without any triggers (should default to function ID)
    [InngestFunction("no-trigger-function", Name = "No Trigger Function")]
    public class NoTriggerFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    #endregion

    [Fact]
    public void RegisterFunction_AutoDerivesTrigger_FromIInngestEventData()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(AutoTriggerFunction));

        // Assert
        var registrations = registry.GetRegistrations().ToList();
        Assert.Single(registrations);

        var registration = registrations[0];
        Assert.Equal("auto-trigger-function", registration.Id);
        Assert.Single(registration.Triggers);
        Assert.Equal("test/order.created", registration.Triggers[0].Event);
    }

    [Fact]
    public void RegisterFunction_DoesNotAutoDerive_WhenExplicitTriggerExists()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(ExplicitTriggerFunction));

        // Assert
        var registrations = registry.GetRegistrations().ToList();
        var registration = registrations[0];

        Assert.Single(registration.Triggers);
        Assert.Equal("custom/explicit.event", registration.Triggers[0].Event); // Should use explicit, not derived
    }

    [Fact]
    public void RegisterFunction_HandlesCronTriggers()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(CronFunction));

        // Assert
        var registration = registry.GetRegistrations().First();
        Assert.Single(registration.Triggers);
        Assert.Equal("cron(0 * * * *)", registration.Triggers[0].Event);
    }

    [Fact]
    public void RegisterFunction_HandlesMultipleTriggers()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(MultiTriggerFunction));

        // Assert
        var registration = registry.GetRegistrations().First();
        Assert.Equal(2, registration.Triggers.Length);
        Assert.Contains(registration.Triggers, t => t.Event == "event/one");
        Assert.Contains(registration.Triggers, t => t.Event == "event/two");
    }

    [Fact]
    public void RegisterFunction_DefaultsToFunctionId_WhenNoTriggers()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(NoTriggerFunction));

        // Assert
        var registration = registry.GetRegistrations().First();
        Assert.Single(registration.Triggers);
        Assert.Equal("no-trigger-function", registration.Triggers[0].Event);
    }

    [Fact]
    public void RegisterFunction_CapturesFunctionOptions()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(ConfiguredFunction));

        // Assert
        var registration = registry.GetRegistrations().First();
        Assert.NotNull(registration.Options);

        // Retry
        Assert.NotNull(registration.Options.Retry);
        Assert.Equal(5, registration.Options.Retry.Attempts);

        // Concurrency
        Assert.NotNull(registration.Options.ConcurrencyOptions);
        Assert.Equal(10, registration.Options.ConcurrencyOptions.Limit);
        Assert.Equal("event.data.userId", registration.Options.ConcurrencyOptions.Key);

        // Rate limit
        Assert.NotNull(registration.Options.RateLimit);
        Assert.Equal(100, registration.Options.RateLimit.Limit);
        Assert.Equal("1h", registration.Options.RateLimit.Period);
    }

    [Fact]
    public void RegisterFunction_CapturesEventDataType()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunction(typeof(AutoTriggerFunction));

        // Assert
        var registration = registry.GetRegistrations().First();
        Assert.NotNull(registration.EventDataType);
        Assert.Equal(typeof(TestOrderEvent), registration.EventDataType);
    }

    [Fact]
    public void RegisterFunctionsFromAssembly_FindsAllFunctions()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        registry.RegisterFunctionsFromAssembly(Assembly.GetExecutingAssembly());

        // Assert
        var registrations = registry.GetRegistrations().ToList();
        Assert.True(registrations.Count >= 6); // At least our test functions
    }

    [Fact]
    public void GetRegistration_ReturnsCorrectFunction()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");
        registry.RegisterFunction(typeof(AutoTriggerFunction));

        // Act
        var registration = registry.GetRegistration("test-app-auto-trigger-function");

        // Assert
        Assert.NotNull(registration);
        Assert.Equal("auto-trigger-function", registration.Id);
    }

    [Fact]
    public void GetRegistration_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var registry = new InngestFunctionRegistry("test-app");

        // Act
        var registration = registry.GetRegistration("non-existent");

        // Assert
        Assert.Null(registration);
    }
}
