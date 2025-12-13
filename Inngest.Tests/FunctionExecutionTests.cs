using System.Text;
using System.Text.Json;
using Inngest.Attributes;
using Inngest.Configuration;
using Inngest.Internal;
using Inngest.Steps;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inngest.Tests;

/// <summary>
/// Tests for function execution via POST requests
/// </summary>
public class FunctionExecutionTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Test Function Definitions

    public record TestExecutionEvent : IInngestEventData
    {
        public static string EventName => "test/execute";
        public string? Value { get; init; }
    }

    [InngestFunction("simple-function", Name = "Simple Function")]
    public class SimpleFunction : IInngestFunction<TestExecutionEvent>
    {
        public Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { result = "success", value = context.Event?.Data?.Value });
        }
    }

    [InngestFunction("step-function", Name = "Step Function")]
    public class StepFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            var step1Result = await context.Step.Run("step-1", () => "step 1 result");
            var step2Result = await context.Step.Run("step-2", () => $"combined: {step1Result}");
            return new { final = step2Result };
        }
    }

    [InngestFunction("error-function", Name = "Error Function")]
    public class ErrorFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Intentional test error");
        }
    }

    [InngestFunction("async-step-function", Name = "Async Step Function")]
    public class AsyncStepFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            var result = await context.Step.Run("async-step", async () =>
            {
                await Task.Delay(10, cancellationToken);
                return 42;
            });
            return new { computed = result };
        }
    }

    [InngestFunction("step-error-function", Name = "Step Error Function")]
    public class StepErrorFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            // This step will throw an exception
            await context.Step.Run("failing-step", () =>
            {
                throw new InvalidOperationException("Step failed intentionally for testing");
            });
            return new { result = "should not reach here" };
        }
    }

    [InngestFunction("step-error-after-success-function", Name = "Step Error After Success Function")]
    public class StepErrorAfterSuccessFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            // First step succeeds
            var step1 = await context.Step.Run("first-step", () => "first step result");

            // Second step fails
            await context.Step.Run("failing-step", () =>
            {
                throw new ArgumentException("Second step failed");
            });
            return new { result = step1 };
        }
    }

    [InngestFunction("step-non-retriable-function", Name = "Step Non Retriable Function")]
    public class StepNonRetriableFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            // This step throws a non-retriable exception
            await context.Step.Run("non-retriable-step", () =>
            {
                throw new Inngest.Exceptions.NonRetriableException("This error should not be retried");
            });
            return new { result = "should not reach here" };
        }
    }

    [InngestFunction("step-retry-after-function", Name = "Step Retry After Function")]
    public class StepRetryAfterFunction : IInngestFunction<TestExecutionEvent>
    {
        public async Task<object?> ExecuteAsync(InngestContext<TestExecutionEvent> context, CancellationToken cancellationToken)
        {
            // This step throws a retry-after exception
            await context.Step.Run("retry-after-step", () =>
            {
                throw new Inngest.Exceptions.RetryAfterException("Rate limited", TimeSpan.FromSeconds(60));
            });
            return new { result = "should not reach here" };
        }
    }

    #endregion

    [Fact]
    public async Task HandlePost_ExecutesFunction_ReturnsResult()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(SimpleFunction));

        var services = new ServiceCollection();
        services.AddSingleton<SimpleFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-simple-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new
            {
                name = "test/execute",
                data = new { value = "hello" }
            },
            events = new[]
            {
                new { name = "test/execute", data = new { value = "hello" } }
            },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-simple-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal("success", response.GetProperty("result").GetString());
        Assert.Equal("hello", response.GetProperty("value").GetString());
    }

    [Fact]
    public async Task HandlePost_WithStep_FirstCallReturnsStepOp()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(206, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement[]>(responseBody, _jsonOptions);

        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("step-1", response[0].GetProperty("id").GetString());
        Assert.Equal("StepRun", response[0].GetProperty("op").GetString());
    }

    [Fact]
    public async Task HandlePost_WithMemoizedSteps_ExecutesNextStep()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        // Second call with step-1 already memoized
        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>
            {
                ["step-1"] = "step 1 result"
            }
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(206, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement[]>(responseBody, _jsonOptions);

        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("step-2", response[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task HandlePost_WithAllStepsMemoized_ReturnsFinalResult()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        // Final call with all steps memoized
        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>
            {
                ["step-1"] = "step 1 result",
                ["step-2"] = "combined: step 1 result"
            }
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal("combined: step 1 result", response.GetProperty("final").GetString());
    }

    [Fact]
    public async Task HandlePost_UnknownFunction_Returns404()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        var client = new InngestClient(
            options,
            registry,
            null,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "unknown-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/event", data = new { } },
            events = new[] { new { name = "test/event", data = new { } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "unknown-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandlePost_FunctionThrowsError_ReturnsErrorResponse()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(ErrorFunction));

        var services = new ServiceCollection();
        services.AddSingleton<ErrorFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-error-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/event", data = new { } },
            events = new[] { new { name = "test/event", data = new { } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-error-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        // Error should result in 500 or error response
        Assert.True(context.Response.StatusCode == 500 || context.Response.StatusCode == 206);
    }

    [Fact]
    public async Task HandlePost_AsyncStep_ExecutesCorrectly()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(AsyncStepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<AsyncStepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-async-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-async-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(206, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement[]>(responseBody, _jsonOptions);

        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("async-step", response[0].GetProperty("id").GetString());
        Assert.Equal(42, response[0].GetProperty("data").GetInt32());
    }

    [Fact]
    public async Task HandlePost_WithAsyncStepMemoized_ReturnsFinalResult()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(AsyncStepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<AsyncStepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-async-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>
            {
                ["async-step"] = 42
            }
        };

        var context = CreateHttpContext("POST", payload, "test-app-async-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal(42, response.GetProperty("computed").GetInt32());
    }

    [Fact]
    public async Task HandlePost_StepThrowsException_Returns500ForRetry()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepErrorFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepErrorFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-error-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-error-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert - Step error should return 500 to trigger retry
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        // Verify error response format (consistent with WriteErrorResponse)
        Assert.Equal("InvalidOperationException", response.GetProperty("name").GetString());
        Assert.Equal("Step failed intentionally for testing", response.GetProperty("message").GetString());
        Assert.True(response.TryGetProperty("stack", out _)); // Stack trace should be present

        // Verify X-Inngest-No-Retry header is set to false (allow retries)
        Assert.True(context.Response.Headers.ContainsKey("X-Inngest-No-Retry"));
        Assert.Equal("false", context.Response.Headers["X-Inngest-No-Retry"].ToString());
    }

    [Fact]
    public async Task HandlePost_StepErrorAfterSuccess_Returns500ForRetry()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepErrorAfterSuccessFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepErrorAfterSuccessFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        // First step is memoized (already succeeded), second step will fail
        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-error-after-success-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>
            {
                ["first-step"] = "first step result"  // Already memoized
            }
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-error-after-success-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert - Step error should return 500 to trigger retry
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        // Verify error response for the second (failing) step
        Assert.Equal("ArgumentException", response.GetProperty("name").GetString());
        Assert.Equal("Second step failed", response.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HandlePost_StepSucceeds_Returns206ForScheduling()
    {
        // Arrange - This verifies that normal step execution still returns 206
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert - Successful step should return 206
        Assert.Equal(206, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement[]>(responseBody, _jsonOptions);

        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("step-1", response[0].GetProperty("id").GetString());
        Assert.Equal("StepRun", response[0].GetProperty("op").GetString());
    }

    [Fact]
    public async Task HandlePost_StepThrowsNonRetriableException_Returns400WithNoRetry()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepNonRetriableFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepNonRetriableFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-non-retriable-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-non-retriable-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert - NonRetriableException should return 400 with X-Inngest-No-Retry: true
        Assert.Equal(400, context.Response.StatusCode);

        // Verify X-Inngest-No-Retry header is set to true (do not retry)
        Assert.True(context.Response.Headers.ContainsKey("X-Inngest-No-Retry"));
        Assert.Equal("true", context.Response.Headers["X-Inngest-No-Retry"].ToString());

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal("This error should not be retried", response.GetProperty("message").GetString());
        Assert.Equal("NonRetriableException", response.GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandlePost_StepThrowsRetryAfterException_Returns500WithRetryAfterHeader()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(StepRetryAfterFunction));

        var services = new ServiceCollection();
        services.AddSingleton<StepRetryAfterFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var payload = new
        {
            ctx = new
            {
                fn_id = "test-app-step-retry-after-function",
                run_id = "run-123",
                step_id = "step"
            },
            @event = new { name = "test/execute", data = new { value = "test" } },
            events = new[] { new { name = "test/execute", data = new { value = "test" } } },
            steps = new Dictionary<string, object>()
        };

        var context = CreateHttpContext("POST", payload, "test-app-step-retry-after-function");

        // Act
        await client.HandleRequestAsync(context);

        // Assert - RetryAfterException should return 500 with Retry-After header
        Assert.Equal(500, context.Response.StatusCode);

        // Verify X-Inngest-No-Retry header is set to false (allow retries)
        Assert.True(context.Response.Headers.ContainsKey("X-Inngest-No-Retry"));
        Assert.Equal("false", context.Response.Headers["X-Inngest-No-Retry"].ToString());

        // Verify Retry-After header is set to 60 seconds
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("60", context.Response.Headers["Retry-After"].ToString());

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal("Rate limited", response.GetProperty("message").GetString());
    }

    #region Helper Methods

    private DefaultHttpContext CreateHttpContext(string method, object? payload = null, string? functionId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.PathBase = "/api/inngest";
        context.Response.Body = new MemoryStream();

        if (functionId != null)
        {
            context.Request.QueryString = new QueryString($"?fnId={functionId}");
        }

        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            context.Request.ContentType = "application/json";
        }

        return context;
    }

    #endregion
}
