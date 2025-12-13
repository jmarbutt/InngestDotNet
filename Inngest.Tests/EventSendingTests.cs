using System.Net;
using System.Text.Json;
using Inngest.Tests.Helpers;

namespace Inngest.Tests;

/// <summary>
/// Tests for event sending functionality
/// </summary>
public class EventSendingTests
{
    [Fact]
    public async Task SendEventAsync_WithNameAndData_SendsCorrectPayload()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-123\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-event-key",
            httpClient: httpClient);

        // Act
        var result = await client.SendEventAsync("test/event.created", new { userId = "123" });

        // Assert
        Assert.True(result);
        Assert.Single(handler.Requests);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/e/test-event-key", request.RequestUri?.ToString());

        var body = await request.Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("test/event.created", events[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendEventAsync_WithInngestEvent_SendsAllFields()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-123\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var evt = new InngestEvent("user/created", new { userId = "456" })
            .WithUser(new { id = "user-789" })
            .WithIdempotencyKey("idem-key-123");

        // Act
        var result = await client.SendEventAsync(evt);

        // Assert
        Assert.True(result);

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.NotNull(events);

        var sentEvent = events[0];
        Assert.Equal("user/created", sentEvent.GetProperty("name").GetString());
        Assert.Equal("456", sentEvent.GetProperty("data").GetProperty("userId").GetString());
        Assert.Equal("user-789", sentEvent.GetProperty("user").GetProperty("id").GetString());
        Assert.Equal("idem-key-123", sentEvent.GetProperty("idempotencyKey").GetString());
        Assert.True(sentEvent.TryGetProperty("ts", out _)); // Timestamp should be present
        Assert.True(sentEvent.TryGetProperty("id", out _)); // ID should be present
    }

    [Fact]
    public async Task SendEventsAsync_SendsMultipleEvents()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-1\",\"evt-2\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var events = new[]
        {
            new InngestEvent("order/created", new { orderId = "1" }),
            new InngestEvent("order/created", new { orderId = "2" })
        };

        // Act
        var result = await client.SendEventsAsync(events);

        // Assert
        Assert.True(result);

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var sentEvents = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.NotNull(sentEvents);
        Assert.Equal(2, sentEvents.Length);
    }

    [Fact]
    public async Task SendEventAsync_InDevModeWithoutEventKey_UsesDummyKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DEV", "true");
        try
        {
            var handler = new MockHttpMessageHandler();
            handler.QueueResponse(HttpStatusCode.OK, "{}");

            var httpClient = new HttpClient(handler);
            var client = new InngestClient(
                eventKey: null, // No event key
                httpClient: httpClient);

            // Act
            await client.SendEventAsync("test/event", new { });

            // Assert
            var request = handler.Requests[0];
            Assert.Contains("/e/dev", request.RequestUri?.ToString()); // Should use "dev" as dummy key
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", null);
        }
    }

    [Fact]
    public async Task SendEventAsync_ReturnsFalseOnFailure()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.Unauthorized, "{\"error\":\"Invalid key\"}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "bad-key",
            httpClient: httpClient);

        // Act
        var result = await client.SendEventAsync("test/event", new { });

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendEventAsync_GeneratesIdIfNotProvided()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var evt = new InngestEvent { Name = "test/event", Data = new { } };
        Assert.Null(evt.Id); // Verify ID is null before sending

        // Act
        await client.SendEventAsync(evt);

        // Assert
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        var sentEvent = events![0];

        Assert.True(sentEvent.TryGetProperty("id", out var idProp));
        Assert.False(string.IsNullOrEmpty(idProp.GetString()));
    }

    [Fact]
    public async Task SendEventAsync_PreservesCustomId()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var evt = new InngestEvent
        {
            Id = "custom-id-123",
            Name = "test/event",
            Data = new { }
        };

        // Act
        await client.SendEventAsync(evt);

        // Assert
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.Equal("custom-id-123", events![0].GetProperty("id").GetString());
    }

    [Fact]
    public void InngestEvent_SetsTimestampAutomatically()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var evt = new InngestEvent("test/event", new { });
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        Assert.InRange(evt.Timestamp, before, after);
    }
}
