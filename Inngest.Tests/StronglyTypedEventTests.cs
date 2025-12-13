using System.Net;
using System.Text.Json;
using Inngest.Attributes;
using Inngest.Tests.Helpers;

namespace Inngest.Tests;

/// <summary>
/// Tests for strongly-typed event functionality
/// </summary>
public class StronglyTypedEventTests
{
    #region Test Event Definitions

    /// <summary>
    /// Test event using IInngestEventData interface
    /// </summary>
    public record OrderCreatedEvent : IInngestEventData
    {
        public static string EventName => "test/order.created";
        public required string OrderId { get; init; }
        public required decimal Amount { get; init; }
    }

    /// <summary>
    /// Test event using attribute-based approach
    /// </summary>
    [InngestEvent("test/user.signup")]
    public record UserSignupEvent
    {
        public required string UserId { get; init; }
        public required string Email { get; init; }
    }

    /// <summary>
    /// Event without any event name definition (should fail)
    /// </summary>
    public record UndefinedEvent
    {
        public string? Data { get; init; }
    }

    #endregion

    #region IInngestEventData Tests

    [Fact]
    public void IInngestEventData_EventName_IsAccessible()
    {
        // Assert
        Assert.Equal("test/order.created", OrderCreatedEvent.EventName);
    }

    [Fact]
    public async Task SendAsync_WithIInngestEventData_UsesCorrectEventName()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-1\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var orderEvent = new OrderCreatedEvent
        {
            OrderId = "order-123",
            Amount = 99.99m
        };

        // Act
        var result = await client.SendAsync(orderEvent);

        // Assert
        Assert.True(result);

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.NotNull(events);

        var sentEvent = events[0];
        Assert.Equal("test/order.created", sentEvent.GetProperty("name").GetString());
        Assert.Equal("order-123", sentEvent.GetProperty("data").GetProperty("orderId").GetString());
        Assert.Equal(99.99m, sentEvent.GetProperty("data").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task SendManyAsync_WithIInngestEventData_SendsMultipleEvents()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-1\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var events = new[]
        {
            new OrderCreatedEvent { OrderId = "order-1", Amount = 10.00m },
            new OrderCreatedEvent { OrderId = "order-2", Amount = 20.00m }
        };

        // Act
        var result = await client.SendManyAsync(events);

        // Assert
        Assert.True(result);

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var sentEvents = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.NotNull(sentEvents);
        Assert.Equal(2, sentEvents.Length);

        // Both should have the same event name
        Assert.All(sentEvents, e => Assert.Equal("test/order.created", e.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task SendAsync_WithConfigure_AllowsEventConfiguration()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-1\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var orderEvent = new OrderCreatedEvent
        {
            OrderId = "order-123",
            Amount = 50.00m
        };

        // Act
        await client.SendAsync(orderEvent, evt =>
        {
            evt.WithIdempotencyKey("idem-123");
            evt.WithUser(new { id = "user-456" });
        });

        // Assert
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        var sentEvent = events![0];

        Assert.Equal("idem-123", sentEvent.GetProperty("idempotencyKey").GetString());
        Assert.Equal("user-456", sentEvent.GetProperty("user").GetProperty("id").GetString());
    }

    #endregion

    #region Attribute-Based Tests

    [Fact]
    public async Task SendByAttributeAsync_UsesAttributeEventName()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.QueueResponse(HttpStatusCode.OK, "{\"ids\":[\"evt-1\"]}");

        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var userEvent = new UserSignupEvent
        {
            UserId = "user-123",
            Email = "test@example.com"
        };

        // Act
        var result = await client.SendByAttributeAsync(userEvent);

        // Assert
        Assert.True(result);

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.Equal("test/user.signup", events![0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendByAttributeAsync_WithoutAttribute_ThrowsException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var client = new InngestClient(
            eventKey: "test-key",
            httpClient: httpClient);

        var undefinedEvent = new UndefinedEvent { Data = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.SendByAttributeAsync(undefinedEvent);
        });
    }

    #endregion

    #region GetEventName Tests

    [Fact]
    public void GetEventName_WithIInngestEventData_ReturnsEventName()
    {
        // Act
        var eventName = InngestClientExtensions.GetEventName<OrderCreatedEvent>();

        // Assert
        Assert.Equal("test/order.created", eventName);
    }

    [Fact]
    public void GetEventName_WithAttribute_ReturnsAttributeName()
    {
        // Act
        var eventName = InngestClientExtensions.GetEventName<UserSignupEvent>();

        // Assert
        Assert.Equal("test/user.signup", eventName);
    }

    [Fact]
    public void GetEventName_WithoutDefinition_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            InngestClientExtensions.GetEventName<UndefinedEvent>();
        });
    }

    #endregion

    #region InngestEventAttribute Tests

    [Fact]
    public void InngestEventAttribute_StoresEventName()
    {
        // Arrange & Act
        var attr = new InngestEventAttribute("custom/event.name");

        // Assert
        Assert.Equal("custom/event.name", attr.Name);
    }

    [Fact]
    public void InngestEventAttribute_WithEmptyName_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new InngestEventAttribute(""));
        Assert.Throws<ArgumentException>(() => new InngestEventAttribute("   "));
    }

    #endregion
}
