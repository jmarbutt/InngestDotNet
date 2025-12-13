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
