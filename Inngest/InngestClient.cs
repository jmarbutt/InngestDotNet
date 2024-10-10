using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Inngest;

public class InngestClient(
    string eventKey,
    string signingKey,
    string apiOrigin = "https://api.inngest.com",
    string eventApiOrigin = "https://inn.gs")
{
    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, Func<InngestContext, Task<object>>> _functions = new();

    public void CreateFunction(string functionId, Func<InngestContext, Task<object>> handler)
    {
        _functions[functionId] = handler;
    }

    public async Task<bool> SendEventAsync(string eventName, object eventData)
    {
        var payload = new InngestEvent
        {
            Name = eventName,
            Data = eventData,
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{eventApiOrigin}/e/{eventKey}", content);

        return response.IsSuccessStatusCode;
    }

    public async Task HandleRequestAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Method == "PUT")
        {
            await HandleSyncRequest(context);
        }
        else if (request.Method == "POST")
        {
            await HandleCallRequest(context);
        }
        else if (request.Method == "GET")
        {
            await HandleIntrospectionRequest(context);
        }
        else
        {
            response.StatusCode = 405; // Method Not Allowed
        }
    }

    private async Task HandleSyncRequest(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Method != "PUT")
        {
            response.StatusCode = 405; // Method Not Allowed
            return;
        }

        // Read the request body
        var requestBody = await new StreamReader(request.Body).ReadToEndAsync();
        var syncPayload = JsonSerializer.Deserialize<SyncPayload>(requestBody);

        // Perform sync logic here
        var syncResult = await SyncFunctions(syncPayload);

        if (syncResult)
        {
            response.StatusCode = 200; // OK
            var responseBody = new { message = "Sync successful", modified = true };
            await response.WriteAsJsonAsync(responseBody);
        }
        else
        {
            response.StatusCode = 500; // Internal Server Error
            var responseBody = new { message = "Sync failed" };
            await response.WriteAsJsonAsync(responseBody);
        }
    }

    private async Task<bool> SyncFunctions(SyncPayload payload)
    {
        // Implement the actual sync logic here
        // This is a placeholder implementation
        await Task.Delay(100); // Simulate some async work
        return true; // Assume sync is always successful
    }

    private class SyncPayload
    {
        public string AppId { get; set; }
        public List<FunctionDefinition> Functions { get; set; }
    }

    private class FunctionDefinition
    {
        public string Id { get; set; }
        public string Code { get; set; }
    }

    private async Task HandleCallRequest(HttpContext context)
    {
        var payload = await JsonSerializer.DeserializeAsync<CallRequestPayload>(context.Request.Body);
        var inngestContext = new InngestContext(payload.Event, payload.Events, payload.Steps, payload.Ctx);

        if (_functions.TryGetValue(payload.Ctx.FunctionId, out var handler))
        {
            try
            {
                var result = await handler(inngestContext);
                await context.Response.WriteAsJsonAsync(result);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
        }
        else
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = "Function not found" });
        }
    }

    private async Task HandleIntrospectionRequest(HttpContext context)
    {
        var response = new
        {
            authentication_succeeded = true,
            api_origin = apiOrigin,
            event_api_origin = eventApiOrigin,
            function_count = _functions.Count,
            has_event_key = !string.IsNullOrEmpty(eventKey),
            has_signing_key = !string.IsNullOrEmpty(signingKey),
            mode = "cloud",
            schema_version = "2024-05-24"
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}