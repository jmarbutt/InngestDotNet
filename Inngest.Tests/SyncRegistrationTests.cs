using System.Net;
using System.Text;
using System.Text.Json;
using Inngest.Attributes;
using Inngest.Configuration;
using Inngest.Internal;
using Inngest.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inngest.Tests;

/// <summary>
/// Tests for sync/registration functionality
/// </summary>
public class SyncRegistrationTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Test Function Definitions

    public record TestEvent : IInngestEventData
    {
        public static string EventName => "test/sync.event";
        public string? Data { get; init; }
    }

    [InngestFunction("sync-test-function", Name = "Sync Test Function")]
    public class SyncTestFunction : IInngestFunction<TestEvent>
    {
        public Task<object?> ExecuteAsync(InngestContext<TestEvent> context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { success = true });
        }
    }

    [InngestFunction("cron-test-function", Name = "Cron Test Function")]
    [CronTrigger("*/5 * * * *")]
    public class CronTestFunction : IInngestFunction
    {
        public Task<object?> ExecuteAsync(InngestContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(new { ran = true });
        }
    }

    #endregion

    [Fact]
    public async Task HandleSync_InBandMode_ReturnsFunctionDefinitions()
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
        registry.RegisterFunction(typeof(SyncTestFunction));

        var services = new ServiceCollection();
        services.AddSingleton<SyncTestFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("PUT");
        context.Request.Headers["X-Inngest-Sync-Kind"] = "in_band";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal("test-app", response.GetProperty("app_id").GetString());
        Assert.True(response.TryGetProperty("functions", out var functions));
        Assert.Equal(1, functions.GetArrayLength());

        var function = functions[0];
        Assert.Equal("test-app-sync-test-function", function.GetProperty("id").GetString());
        Assert.Equal("Sync Test Function", function.GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleSync_InBandMode_IncludesTriggers()
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
        registry.RegisterFunction(typeof(SyncTestFunction));

        var services = new ServiceCollection();
        services.AddSingleton<SyncTestFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("PUT");
        context.Request.Headers["X-Inngest-Sync-Kind"] = "in_band";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        var function = response.GetProperty("functions")[0];
        var triggers = function.GetProperty("triggers");
        Assert.Equal(1, triggers.GetArrayLength());

        var trigger = triggers[0];
        Assert.Equal("test/sync.event", trigger.GetProperty("event").GetString());
    }

    [Fact]
    public async Task HandleSync_CronTrigger_SerializesCorrectly()
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
        registry.RegisterFunction(typeof(CronTestFunction));

        var services = new ServiceCollection();
        services.AddSingleton<CronTestFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("PUT");
        context.Request.Headers["X-Inngest-Sync-Kind"] = "in_band";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        var function = response.GetProperty("functions")[0];
        var triggers = function.GetProperty("triggers");
        var trigger = triggers[0];

        Assert.True(trigger.TryGetProperty("cron", out var cronProp));
        Assert.Equal("*/5 * * * *", cronProp.GetString());
    }

    [Fact]
    public async Task HandleSync_DisableCronInDev_FiltersCronTriggers()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-key",
            DisableCronTriggersInDev = true
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        registry.RegisterFunction(typeof(CronTestFunction));
        registry.RegisterFunction(typeof(SyncTestFunction));

        var services = new ServiceCollection();
        services.AddSingleton<CronTestFunction>();
        services.AddSingleton<SyncTestFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("PUT");
        context.Request.Headers["X-Inngest-Sync-Kind"] = "in_band";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        var functions = response.GetProperty("functions");
        // Only the event-triggered function should be present, not the cron function
        Assert.Equal(1, functions.GetArrayLength());
        Assert.Equal("test-app-sync-test-function", functions[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task HandleSync_InBandMode_IncludesInspection()
    {
        // Arrange
        var options = new InngestOptions
        {
            AppId = "test-app",
            IsDev = true,
            EventKey = "test-event-key",
            SigningKey = "signkey-test-abc123"
        };
        options.ApplyEnvironmentFallbacks();

        var registry = new InngestFunctionRegistry(options.AppId!);
        var client = new InngestClient(
            options,
            registry,
            null,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("PUT");
        context.Request.Headers["X-Inngest-Sync-Kind"] = "in_band";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"url\":\"http://localhost:5000/api/inngest\"}"));

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.True(response.TryGetProperty("inspection", out var inspection));
        Assert.Equal("test-app", inspection.GetProperty("app_id").GetString());
        Assert.True(inspection.GetProperty("has_event_key").GetBoolean());
        Assert.True(inspection.GetProperty("has_signing_key").GetBoolean());
        Assert.Equal("dev", inspection.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task HandleIntrospection_ReturnsBasicInfo()
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
        registry.RegisterFunction(typeof(SyncTestFunction));

        var services = new ServiceCollection();
        services.AddSingleton<SyncTestFunction>();
        var serviceProvider = services.BuildServiceProvider();

        var client = new InngestClient(
            options,
            registry,
            serviceProvider,
            new HttpClient(),
            NullLogger<InngestClient>.Instance);

        var context = CreateHttpContext("GET");

        // Act
        await client.HandleRequestAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<JsonElement>(responseBody, _jsonOptions);

        Assert.Equal(1, response.GetProperty("function_count").GetInt32());
        Assert.True(response.GetProperty("has_event_key").GetBoolean());
        Assert.Equal("dev", response.GetProperty("mode").GetString());
    }

    #region Helper Methods

    private static DefaultHttpContext CreateHttpContext(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.PathBase = "/api/inngest";
        context.Response.Body = new MemoryStream();
        return context;
    }

    #endregion
}
