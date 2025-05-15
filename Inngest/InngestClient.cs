using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Inngest;

/// <summary>
/// Client for interacting with the Inngest service
/// </summary>
public class InngestClient : IInngestClient
{
    private readonly string _eventKey;
    private readonly string _signingKey;
    private readonly string _apiOrigin;
    private readonly string _eventApiOrigin;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, FunctionDefinition> _functions = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, string> _secrets = new();

    /// <summary>
    /// Initialize a new Inngest client
    /// </summary>
    /// <param name="eventKey">Your Inngest event key</param>
    /// <param name="signingKey">Your Inngest signing key</param>
    /// <param name="apiOrigin">Custom API origin (for development)</param>
    /// <param name="eventApiOrigin">Custom event API origin (for development)</param>
    /// <param name="httpClient">Optional custom HttpClient</param>
    public InngestClient(
        string eventKey,
        string signingKey,
        string apiOrigin = "https://api.inngest.com",
        string eventApiOrigin = "https://inn.gs",
        HttpClient? httpClient = null)
    {
        _eventKey = eventKey;
        _signingKey = signingKey;
        _apiOrigin = apiOrigin;
        _eventApiOrigin = eventApiOrigin;
        _httpClient = httpClient ?? new HttpClient();
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
    
    /// <summary>
    /// Add a secret that will be accessible to your functions at runtime
    /// </summary>
    public void AddSecret(string key, string value)
    {
        _secrets[key] = value;
    }

    /// <summary>
    /// Register a new function using the simplified API
    /// </summary>
    public void CreateFunction(string functionId, Func<InngestContext, Task<object>> handler)
    {
        // Create a basic function with the provided ID that triggers on a matching event name
        var trigger = FunctionTrigger.CreateEventTrigger(functionId);
        var functionDefinition = new FunctionDefinition(
            functionId,         // id
            functionId,         // name
            [trigger],  // triggers
            handler,            // handler
            null                // options
        );
        
        _functions[functionId] = functionDefinition;
    }
    
    /// <summary>
    /// Register a new function with full configuration options
    /// </summary>
    public void CreateFunction(string id, string name, FunctionTrigger[] triggers, Func<InngestContext, Task<object>> handler, FunctionOptions? options = null)
    {
        var functionDefinition = new FunctionDefinition(id, name, triggers, handler, options);
        _functions[id] = functionDefinition;
    }

    /// <summary>
    /// Send an event to Inngest
    /// </summary>
    public async Task<bool> SendEventAsync(string eventName, object eventData)
    {
        var evt = new InngestEvent
        {
            Name = eventName,
            Data = eventData,
            Id = Guid.NewGuid().ToString()
        };
        
        return await SendEventAsync(evt);
    }
    
    /// <summary>
    /// Send a pre-configured event to Inngest
    /// </summary>
    public async Task<bool> SendEventAsync(InngestEvent evt)
    {
        // Ensure required fields
        evt.Id ??= Guid.NewGuid().ToString();
        
        var payload = new { events = new[] { evt } };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_eventApiOrigin}/e/{_eventKey}", content);

        return response.IsSuccessStatusCode;
    }
    
    /// <summary>
    /// Send multiple events to Inngest in a single request
    /// </summary>
    public async Task<bool> SendEventsAsync(IEnumerable<InngestEvent> events)
    {
        // Ensure required fields for all events
        var eventsArray = events.Select(evt =>
        {
            evt.Id ??= Guid.NewGuid().ToString();
            return evt;
        }).ToArray();
        
        var payload = new { events = eventsArray };
        var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_eventApiOrigin}/e/{_eventKey}", content);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Handle incoming requests from the Inngest service
    /// </summary>
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
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        }
    }

    private async Task HandleSyncRequest(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Method != "PUT")
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        // Read the request body
        var requestBody = await new StreamReader(request.Body).ReadToEndAsync();
        var syncPayload = JsonSerializer.Deserialize<SyncRequestPayload>(requestBody, _jsonOptions);

        if (syncPayload == null)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { message = "Invalid sync payload" });
            return;
        }

        // Build function definitions for sync
        var functionDefinitions = _functions.Values.Select(fn => new FunctionDefinitionResponse
        {
            Id = fn.Id,
            Name = fn.Name,
            Triggers = fn.Triggers.Select(t => new TriggerDefinitionResponse 
            { 
                Event = t.Event,
                Constraint = t.Constraint?.Expression
            }).ToArray(),
            Steps = new Dictionary<string, object>(),
            Options = fn.Options != null ? new FunctionOptionsResponse
            {
                Concurrency = fn.Options.Concurrency,
                Retry = fn.Options.Retry != null ? new RetryOptionsResponse
                {
                    Attempts = fn.Options.Retry.Attempts,
                    Interval = fn.Options.Retry.Interval,
                    Factor = fn.Options.Retry.Factor,
                    MaxInterval = fn.Options.Retry.MaxInterval
                } : null,
                Queue = fn.Options.Queue,
                Env = fn.Options.Env,
                RateLimit = fn.Options.RateLimit,
                Idempotency = fn.Options.Idempotency
            } : null
        }).ToArray();

        var syncResult = new SyncResponsePayload
        {
            AppId = syncPayload.AppId,
            Functions = functionDefinitions,
            Modified = true
        };

        response.StatusCode = StatusCodes.Status200OK;
        await response.WriteAsJsonAsync(syncResult, _jsonOptions);
    }

    private class SyncRequestPayload
    {
        [JsonPropertyName("app_id")]
        public string? AppId { get; set; }
        
        [JsonPropertyName("functions")]
        public List<FunctionInfo>? Functions { get; set; }
        
        public class FunctionInfo
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
            
            [JsonPropertyName("code")]
            public string? Code { get; set; }
        }
    }

    private class SyncResponsePayload
    {
        [JsonPropertyName("app_id")]
        public string? AppId { get; set; }
        
        [JsonPropertyName("functions")]
        public FunctionDefinitionResponse[]? Functions { get; set; }
        
        [JsonPropertyName("modified")]
        public bool Modified { get; set; }
    }
    
    private class FunctionDefinitionResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("triggers")]
        public TriggerDefinitionResponse[] Triggers { get; set; } = Array.Empty<TriggerDefinitionResponse>();
        
        [JsonPropertyName("steps")]
        public Dictionary<string, object> Steps { get; set; } = new();
        
        [JsonPropertyName("options")]
        public FunctionOptionsResponse? Options { get; set; }
    }
    
    private class TriggerDefinitionResponse
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;
        
        [JsonPropertyName("constraint")]
        public string? Constraint { get; set; }
    }
    
    private class FunctionOptionsResponse
    {
        [JsonPropertyName("concurrency")]
        public int? Concurrency { get; set; }
        
        [JsonPropertyName("retry")]
        public RetryOptionsResponse? Retry { get; set; }
        
        [JsonPropertyName("queue")]
        public string? Queue { get; set; }
        
        [JsonPropertyName("env")]
        public Dictionary<string, string>? Env { get; set; }
        
        [JsonPropertyName("rateLimit")]
        public int? RateLimit { get; set; }
        
        [JsonPropertyName("idempotency")]
        public Dictionary<string, string>? Idempotency { get; set; }
    }
    
    private class RetryOptionsResponse
    {
        [JsonPropertyName("attempts")]
        public int? Attempts { get; set; }
        
        [JsonPropertyName("interval")]
        public int? Interval { get; set; }
        
        [JsonPropertyName("factor")]
        public double? Factor { get; set; }
        
        [JsonPropertyName("maxInterval")]
        public int? MaxInterval { get; set; }
    }

    private async Task HandleCallRequest(HttpContext context)
    {
        try 
        {
            var payload = await JsonSerializer.DeserializeAsync<CallRequestPayload>(
                context.Request.Body, 
                _jsonOptions);
                
            if (payload == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid request payload" });
                return;
            }

            var inngestContext = new InngestContext(
                payload.Event, 
                payload.Events, 
                payload.Steps,
                payload.Ctx,
                payload.Secrets ?? _secrets,
                this);

            if (_functions.TryGetValue(payload.Ctx.FunctionId, out var function))
            {
                try
                {
                    var result = await function.Handler(inngestContext);
                    await context.Response.WriteAsJsonAsync(result, _jsonOptions);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace });
                }
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = $"Function '{payload.Ctx.FunctionId}' not found" });
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = $"Failed to process request: {ex.Message}" });
        }
    }

    private async Task HandleIntrospectionRequest(HttpContext context)
    {
        var responseObj = new
        {
            authentication_succeeded = true,
            api_origin = _apiOrigin,
            event_api_origin = _eventApiOrigin,
            function_count = _functions.Count,
            has_event_key = !string.IsNullOrEmpty(_eventKey),
            has_signing_key = !string.IsNullOrEmpty(_signingKey),
            mode = "cloud",
            schema_version = "2024-05-15"
        };

        await context.Response.WriteAsJsonAsync(responseObj, _jsonOptions);
    }
}