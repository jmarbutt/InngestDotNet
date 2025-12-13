using System.Text.Json;
using Inngest.Steps;

namespace Inngest.Tests;

public class StepToolsTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Run_WhenStepNotMemoized_ThrowsStepInterruptException()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.Run("test-step", async () =>
            {
                await Task.Delay(1);
                return "test result";
            });
        });

        Assert.Single(exception.Operations);
        Assert.Equal("test-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.StepRun, exception.Operations[0].Op);
        Assert.Equal("test result", exception.Operations[0].Data);
    }

    [Fact]
    public async Task Run_WhenStepMemoized_ReturnsCachedResult()
    {
        // Arrange
        var steps = new Dictionary<string, object>
        {
            ["test-step"] = JsonSerializer.SerializeToElement("cached result", _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);
        var handlerCalled = false;

        // Act
        var result = await stepTools.Run("test-step", () =>
        {
            handlerCalled = true;
            return "new result";
        });

        // Assert
        Assert.Equal("cached result", result);
        Assert.False(handlerCalled, "Handler should not be called when step is memoized");
    }

    [Fact]
    public async Task Run_WhenStepMemoizedAsJsonElement_DeserializesCorrectly()
    {
        // Arrange
        var cachedData = new { name = "test", value = 42 };
        var steps = new Dictionary<string, object>
        {
            ["test-step"] = JsonSerializer.SerializeToElement(cachedData, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.Run<TestData>("test-step", () => new TestData());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task Run_Sync_WhenStepNotMemoized_ThrowsStepInterruptException()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.Run("sync-step", () => 123);
        });

        Assert.Single(exception.Operations);
        Assert.Equal("sync-step", exception.Operations[0].Id);
        Assert.Equal(123, exception.Operations[0].Data);
    }

    [Fact]
    public async Task Run_WhenHandlerThrows_ThrowsStepInterruptExceptionWithError()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            Func<string> handler = () => throw new InvalidOperationException("Test error message");
            await stepTools.Run("error-step", handler);
        });

        Assert.Single(exception.Operations);
        Assert.Equal("error-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.StepError, exception.Operations[0].Op);
        Assert.NotNull(exception.Operations[0].Error);
        Assert.Equal("InvalidOperationException", exception.Operations[0].Error.Name);
        Assert.Equal("Test error message", exception.Operations[0].Error.Message);
    }

    [Fact]
    public async Task Sleep_WhenNotMemoized_ThrowsStepInterruptException()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.Sleep("sleep-step", "5m");
        });

        Assert.Single(exception.Operations);
        Assert.Equal("sleep-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.Sleep, exception.Operations[0].Op);

        var opts = exception.Operations[0].Opts as SleepOpts;
        Assert.NotNull(opts);
        Assert.Equal("5m", opts.Duration);
    }

    [Fact]
    public async Task Sleep_WhenMemoized_ReturnsImmediately()
    {
        // Arrange
        var steps = new Dictionary<string, object>
        {
            ["sleep-step"] = JsonSerializer.SerializeToElement<object?>(null, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act - Should not throw
        await stepTools.Sleep("sleep-step", "5m");

        // Assert - If we get here, the sleep was skipped as expected
        Assert.True(true);
    }

    [Fact]
    public async Task Sleep_WithTimeSpan_FormatsCorrectly()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.Sleep("sleep-step", TimeSpan.FromHours(2) + TimeSpan.FromMinutes(30));
        });

        var opts = exception.Operations[0].Opts as SleepOpts;
        Assert.NotNull(opts);
        Assert.Equal("2h30m", opts.Duration);
    }

    [Fact]
    public async Task SleepUntil_ThrowsStepInterruptExceptionWithISODate()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);
        var futureDate = new DateTimeOffset(2025, 12, 25, 10, 0, 0, TimeSpan.Zero);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.SleepUntil("sleep-until-step", futureDate);
        });

        var opts = exception.Operations[0].Opts as SleepOpts;
        Assert.NotNull(opts);
        Assert.Contains("2025-12-25", opts.Duration);
    }

    [Fact]
    public async Task WaitForEvent_WhenNotMemoized_ThrowsStepInterruptException()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.WaitForEvent<TestEvent>("wait-step", new WaitForEventOptions
            {
                Event = "test/event.received",
                Timeout = "1h",
                Match = "async.data.id == event.data.id"
            });
        });

        Assert.Single(exception.Operations);
        Assert.Equal("wait-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.WaitForEvent, exception.Operations[0].Op);

        var opts = exception.Operations[0].Opts as WaitForEventOpts;
        Assert.NotNull(opts);
        Assert.Equal("test/event.received", opts.Event);
        Assert.Equal("1h", opts.Timeout);
        Assert.Equal("async.data.id == event.data.id", opts.If);
    }

    [Fact]
    public async Task WaitForEvent_WhenMemoizedWithNull_ReturnsNull()
    {
        // Arrange - null indicates timeout
        var steps = new Dictionary<string, object>
        {
            ["wait-step"] = JsonSerializer.SerializeToElement<object?>(null, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.WaitForEvent<TestEvent>("wait-step", new WaitForEventOptions
        {
            Event = "test/event",
            Timeout = "1h"
        });

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForEvent_WhenMemoizedWithEvent_ReturnsEvent()
    {
        // Arrange
        var eventData = new TestEvent { Id = "evt-123", Message = "Hello" };
        var steps = new Dictionary<string, object>
        {
            ["wait-step"] = JsonSerializer.SerializeToElement(eventData, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.WaitForEvent<TestEvent>("wait-step", new WaitForEventOptions
        {
            Event = "test/event",
            Timeout = "1h"
        });

        // Assert
        Assert.NotNull(result);
        Assert.Equal("evt-123", result.Id);
        Assert.Equal("Hello", result.Message);
    }

    [Fact]
    public async Task Invoke_WhenNotMemoized_ThrowsStepInterruptException()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.Invoke<TestResult>("invoke-step", new InvokeOptions
            {
                FunctionId = "other-function",
                Data = new { orderId = "123" },
                Timeout = "30m"
            });
        });

        Assert.Single(exception.Operations);
        Assert.Equal("invoke-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.InvokeFunction, exception.Operations[0].Op);

        var opts = exception.Operations[0].Opts as InvokeFunctionOpts;
        Assert.NotNull(opts);
        Assert.Equal("other-function", opts.FunctionId);
        Assert.Equal("30m", opts.Timeout);
    }

    [Fact]
    public async Task SendEvent_WhenNotMemoized_SendsEventsAndReturnsStepRun()
    {
        // Arrange
        var steps = new Dictionary<string, object>();
        var sentEvents = new List<InngestEvent>();

        // Mock event sender that captures events and returns IDs
        Task<string[]> mockSendEvents(InngestEvent[] events)
        {
            sentEvents.AddRange(events);
            return Task.FromResult(events.Select((_, i) => $"evt-{i + 1}").ToArray());
        }

        var stepTools = new StepTools(steps, _jsonOptions, mockSendEvents);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<StepInterruptException>(async () =>
        {
            await stepTools.SendEvent("send-step",
                new InngestEvent("user/created", new { userId = "123" }),
                new InngestEvent("email/send", new { to = "test@example.com" }));
        });

        // Verify events were sent
        Assert.Equal(2, sentEvents.Count);
        Assert.Equal("user/created", sentEvents[0].Name);
        Assert.Equal("email/send", sentEvents[1].Name);

        // Verify the step operation format
        Assert.Single(exception.Operations);
        Assert.Equal("send-step", exception.Operations[0].Id);
        Assert.Equal(StepOpCode.StepRun, exception.Operations[0].Op);
        Assert.Equal("sendEvent", exception.Operations[0].Name);

        // Verify the data contains the event IDs
        var data = exception.Operations[0].Data;
        Assert.NotNull(data);
    }

    [Fact]
    public async Task SendEvent_WithoutEventSender_ThrowsInvalidOperationException()
    {
        // Arrange - no event sender delegate
        var steps = new Dictionary<string, object>();
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await stepTools.SendEvent("send-step", new InngestEvent("test/event", new { }));
        });

        Assert.Contains("event sender delegate", exception.Message);
    }

    [Fact]
    public async Task SendEvent_WhenMemoized_ReturnsEventIds()
    {
        // Arrange - Inngest returns { ids: [...] } format, not raw string[]
        var memoizedResult = new { ids = new[] { "evt-1", "evt-2" } };
        var steps = new Dictionary<string, object>
        {
            ["send-step"] = JsonSerializer.SerializeToElement(memoizedResult, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.SendEvent("send-step",
            new InngestEvent("test/event", new { }));

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("evt-1", result[0]);
        Assert.Equal("evt-2", result[1]);
    }

    [Fact]
    public async Task SendEvent_WhenMemoizedEmpty_ReturnsEmptyArray()
    {
        // Arrange - Inngest returns { ids: [] } for empty result
        var memoizedResult = new { ids = Array.Empty<string>() };
        var steps = new Dictionary<string, object>
        {
            ["send-step"] = JsonSerializer.SerializeToElement(memoizedResult, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.SendEvent("send-step",
            new InngestEvent("test/event", new { }));

        // Assert
        Assert.Empty(result);
    }

    #region Executor Protocol V1/V2 Format Tests

    [Fact]
    public async Task Run_WhenMemoizedWithDataWrapper_DeserializesCorrectly()
    {
        // Arrange - V1/V2 executor sends { type: "data", data: <value> }
        var wrappedResult = new { type = "data", data = "wrapped result" };
        var steps = new Dictionary<string, object>
        {
            ["test-step"] = JsonSerializer.SerializeToElement(wrappedResult, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.Run("test-step", () => "ignored");

        // Assert
        Assert.Equal("wrapped result", result);
    }

    [Fact]
    public async Task Run_WhenMemoizedWithComplexDataWrapper_DeserializesCorrectly()
    {
        // Arrange - V1/V2 executor sends complex objects wrapped
        var complexData = new { name = "test", value = 123 };
        var wrappedResult = new { type = "data", data = complexData };
        var steps = new Dictionary<string, object>
        {
            ["test-step"] = JsonSerializer.SerializeToElement(wrappedResult, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.Run<TestData>("test-step", () => new TestData());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public async Task WaitForEvent_WhenMemoizedWithDataWrapper_DeserializesCorrectly()
    {
        // Arrange - V1/V2 executor wraps waitForEvent results
        var eventData = new TestEvent { Id = "evt-wrapped", Message = "wrapped event" };
        var wrappedResult = new { type = "data", data = eventData };
        var steps = new Dictionary<string, object>
        {
            ["wait-step"] = JsonSerializer.SerializeToElement(wrappedResult, _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.WaitForEvent<TestEvent>("wait-step", new WaitForEventOptions
        {
            Event = "test/event",
            Timeout = "1h"
        });

        // Assert
        Assert.NotNull(result);
        Assert.Equal("evt-wrapped", result.Id);
        Assert.Equal("wrapped event", result.Message);
    }

    [Fact]
    public async Task Run_WhenMemoizedWithRawValue_StillWorks()
    {
        // Arrange - V0 executor sends raw values (backward compatibility)
        var steps = new Dictionary<string, object>
        {
            ["test-step"] = JsonSerializer.SerializeToElement("raw value", _jsonOptions)
        };
        var stepTools = new StepTools(steps, _jsonOptions);

        // Act
        var result = await stepTools.Run("test-step", () => "ignored");

        // Assert
        Assert.Equal("raw value", result);
    }

    #endregion

    // Test helper classes
    private class TestData
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private class TestEvent
    {
        public string? Id { get; set; }
        public string? Message { get; set; }
    }

    private class TestResult
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
    }
}
