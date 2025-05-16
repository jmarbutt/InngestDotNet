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
    private readonly string? _signingKeyFallback;
    private readonly string _apiOrigin;
    private readonly string _eventApiOrigin;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, FunctionDefinition> _functions = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, string> _secrets = new();
    private readonly string _environment;
    private readonly bool _isDev;
    private readonly string _sdkVersion = "0.1.0";
    private readonly string _appId = "inngest-dotnet";

    /// <summary>
    /// Initialize a new Inngest client
    /// </summary>
    /// <param name="eventKey">Your Inngest event key (defaults to INNGEST_EVENT_KEY environment variable)</param>
    /// <param name="signingKey">Your Inngest signing key (defaults to INNGEST_SIGNING_KEY environment variable)</param>
    /// <param name="apiOrigin">Custom API origin (defaults to environment or Inngest Cloud)</param>
    /// <param name="eventApiOrigin">Custom event API origin (defaults to environment or Inngest Cloud)</param>
    /// <param name="httpClient">Optional custom HttpClient</param>
    public InngestClient(
        string? eventKey = null,
        string? signingKey = null,
        string? apiOrigin = null,
        string? eventApiOrigin = null,
        HttpClient? httpClient = null)
    {
        // Check environment variables according to SDK spec
        _eventKey = eventKey ?? Environment.GetEnvironmentVariable("INNGEST_EVENT_KEY") ?? "";
        _signingKey = signingKey ?? Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY") ?? "";
        _signingKeyFallback = Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY_FALLBACK");
        _environment = Environment.GetEnvironmentVariable("INNGEST_ENV") ?? "dev";
        _isDev = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INNGEST_DEV"));
        
        // Set API endpoints based on dev mode and environment variables
        string devServerUrl = Environment.GetEnvironmentVariable("INNGEST_DEV") ?? "http://localhost:8288";
        if (!Uri.TryCreate(devServerUrl, UriKind.Absolute, out _))
        {
            devServerUrl = "http://localhost:8288";
        }

        _apiOrigin = apiOrigin ?? 
                     Environment.GetEnvironmentVariable("INNGEST_API_BASE_URL") ??
                     (_isDev ? devServerUrl : "https://api.inngest.com");
        
        _eventApiOrigin = eventApiOrigin ?? 
                          Environment.GetEnvironmentVariable("INNGEST_EVENT_API_BASE_URL") ?? 
                          (_isDev ? devServerUrl : "https://inn.gs");
        
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
    /// <returns>The function definition for chaining step definitions</returns>
    public FunctionDefinition CreateFunction(string functionId, Func<InngestContext, Task<object>> handler)
    {
        // Create a basic function with the provided ID that triggers on a matching event name
        var trigger = FunctionTrigger.CreateEventTrigger(functionId);
        var functionDefinition = new FunctionDefinition(
            functionId,         // id
            functionId,         // name
            [trigger],          // triggers
            handler,            // handler
            null                // options
        );
        
        _functions[functionId] = functionDefinition;
        return functionDefinition;
    }
    
    /// <summary>
    /// Register a new function with full configuration options
    /// </summary>
    /// <returns>The function definition for chaining step definitions</returns>
    public FunctionDefinition CreateFunction(string id, string name, FunctionTrigger[] triggers, Func<InngestContext, Task<object>> handler, FunctionOptions? options = null)
    {
        var functionDefinition = new FunctionDefinition(id, name, triggers, handler, options);
        _functions[id] = functionDefinition;
        return functionDefinition;
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

        // Add required headers to the response
        response.Headers["X-Inngest-Sdk"] = $"inngest-dotnet:v{_sdkVersion}";
        response.Headers["X-Inngest-Req-Version"] = "1";

        // Verify the request if signatures are required
        if (!_isDev && !await VerifySignature(context))
        {
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new { error = "Invalid signature" });
            return;
        }

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

    private async Task<bool> VerifySignature(HttpContext context)
    {
        // Skip verification for dev server
        if (_isDev)
        {
            return true;
        }

        var request = context.Request;
        
        // Check if we have a signing key
        if (string.IsNullOrEmpty(_signingKey) && string.IsNullOrEmpty(_signingKeyFallback))
        {
            return false;
        }
        
        // Get the signature from the header
        if (!request.Headers.TryGetValue("X-Inngest-Signature", out var signatureHeader))
        {
            return false;
        }
        
        string signature = signatureHeader.ToString();
        
        // Parse the signature components (t=timestamp&s=signature)
        var components = System.Web.HttpUtility.ParseQueryString(signature);
        if (!long.TryParse(components["t"], out long timestamp))
        {
            return false;
        }
        
        string signatureValue = components["s"] ?? "";
        
        // Verify timestamp is not too old (within last 5 minutes)
        var timestampDateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (DateTimeOffset.UtcNow.Subtract(timestampDateTime).TotalMinutes > 5)
        {
            return false;
        }
        
        // Read the request body
        request.EnableBuffering();
        
        string requestBody;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync();
        }
        
        // Reset the position so it can be read again in the handler
        request.Body.Position = 0;
        
        // Try to verify with primary signing key
        bool verified = VerifyHmacSha256(requestBody + timestamp, _signingKey, signatureValue);
        
        // If primary fails and we have a fallback, try that
        if (!verified && !string.IsNullOrEmpty(_signingKeyFallback))
        {
            verified = VerifyHmacSha256(requestBody + timestamp, _signingKeyFallback, signatureValue);
        }
        
        return verified;
    }
    
    private bool VerifyHmacSha256(string data, string key, string expectedSignature)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        string computedSignature = Convert.ToHexString(hashBytes).ToLower();
        
        return computedSignature == expectedSignature.ToLower();
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

        // Check for deployId query parameter
        string? deployId = null;
        if (request.Query.TryGetValue("deployId", out var deployIdValues))
        {
            deployId = deployIdValues.FirstOrDefault();
        }

        // Determine URL that Inngest should use to reach this service
        var serveOrigin = Environment.GetEnvironmentVariable("INNGEST_SERVE_ORIGIN");
        var servePath = Environment.GetEnvironmentVariable("INNGEST_SERVE_PATH");
        
        // Try to determine the URL that Inngest can use to reach this service
        string url;
        if (!string.IsNullOrEmpty(serveOrigin))
        {
            url = string.IsNullOrEmpty(servePath) 
                ? serveOrigin 
                : $"{serveOrigin.TrimEnd('/')}/{servePath.TrimStart('/')}";
        }
        else
        {
            // Try to infer from request
            var host = request.Headers.Host.ToString() ?? "localhost";
            var scheme = request.Headers.ContainsKey("X-Forwarded-Proto") 
                ? request.Headers["X-Forwarded-Proto"].ToString() 
                : request.Scheme;
                
            // If host doesn't include a protocol, add it
            if (!host.StartsWith("http://") && !host.StartsWith("https://"))
            {
                host = $"{scheme}://{host}";
            }
            
            // Use the full request path if servePath is not specified
            var path = string.IsNullOrEmpty(servePath) 
                ? request.PathBase.ToString() 
                : servePath;
            
            // Ensure path starts with a slash if it's not empty
            if (!string.IsNullOrEmpty(path) && !path.StartsWith("/"))
            {
                path = $"/{path}";
            }
            
            url = $"{host}{path}";
        }

        // Build up the list of functions to register
        var fnArray = new List<object>();
        
        foreach (var fn in _functions.Values)
        {
            var triggers = new List<object>();
            foreach (var trigger in fn.Triggers)
            {
                if (trigger.Event.StartsWith("cron(") && trigger.Event.EndsWith(")"))
                {
                    // Handle cron triggers
                    var cronExpression = trigger.Event.Substring(5, trigger.Event.Length - 6);
                    triggers.Add(new { cron = cronExpression });
                }
                else
                {
                    // Handle event triggers
                    var triggerObj = new Dictionary<string, string>();
                    triggerObj["event"] = trigger.Event;
                    
                    if (trigger.Constraint?.Expression != null)
                    {
                        triggerObj["expression"] = trigger.Constraint.Expression;
                    }
                    
                    triggers.Add(triggerObj);
                }
            }
            
            var retries = fn.Options?.Retry != null 
                ? new { attempts = fn.Options.Retry.Attempts ?? 4 } 
                : null;
                
            var functionObj = new
            {
                id = $"{_appId}-{fn.Id}", // Composite ID
                name = fn.Name,
                triggers = triggers,
                steps = new
                {
                    step = new
                    {
                        id = "step",
                        name = "step",
                        runtime = new
                        {
                            type = "http",
                            url = $"{url.TrimEnd('/')}?stepId=step&fnId={fn.Id}" // Use the base URL from the request path or INNGEST_SERVE_PATH
                        },
                        retries = retries
                    }
                }
            };
            
            fnArray.Add(functionObj);
        }

        // Create register payload according to the SDK specification
        var registerPayload = new
        {
            url = url,
            deployType = "ping",
            appName = _appId,
            sdk = $"inngest-dotnet:v{_sdkVersion}",
            v = "0.1",
            framework = "aspnetcore",
            functions = fnArray
        };
        
        // Log information about registration for debugging
        Console.WriteLine($"Registering Inngest functions with URL: {url}");

        // Prepare the register URL
        var registerUrl = $"{_apiOrigin}/fn/register";
        if (!string.IsNullOrEmpty(deployId))
        {
            registerUrl = $"{registerUrl}?deployId={deployId}";
        }
        
        // Add the authorization header if we're not in dev mode
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, registerUrl);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(registerPayload, _jsonOptions),
            Encoding.UTF8,
            "application/json");
        
        requestMessage.Headers.Add("X-Inngest-Sdk", $"inngest-dotnet:v{_sdkVersion}");
        
        if (!_isDev && !string.IsNullOrEmpty(_signingKey))
        {
            // Create a bearer token from the hashed signing key as per spec
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var keyBytes = Encoding.UTF8.GetBytes(_signingKey.Replace("signkey-", ""));
            var hashBytes = sha256.ComputeHash(keyBytes);
            var hashedKey = $"signkey-{_signingKey.Split('-')[1]}-{Convert.ToHexString(hashBytes).ToLower()}";
            requestMessage.Headers.Add("Authorization", $"Bearer {hashedKey}");
        }
        
        // Add expected server kind if we got it from the request
        if (request.Headers.TryGetValue("X-Inngest-Server-Kind", out var serverKindValues))
        {
            var serverKind = serverKindValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(serverKind))
            {
                requestMessage.Headers.Add("X-Inngest-Expected-Server-Kind", serverKind);
            }
        }
        
        var registerResponse = await _httpClient.SendAsync(requestMessage);

        if (registerResponse.IsSuccessStatusCode)
        {
            var responseContent = await registerResponse.Content.ReadAsStringAsync();
            var syncResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, _jsonOptions);
            bool modified = false;
            
            if (syncResponse != null && syncResponse.TryGetValue("modified", out var modifiedObj))
            {
                modified = modifiedObj is JsonElement element && element.ValueKind == JsonValueKind.True;
            }

            response.StatusCode = StatusCodes.Status200OK;
            await response.WriteAsJsonAsync(new 
            { 
                message = "Successfully synced.",
                modified = modified
            }, _jsonOptions);
        }
        else
        {
            var errorContent = await registerResponse.Content.ReadAsStringAsync();
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new 
            { 
                message = $"Sync failed: {errorContent}",
                modified = false
            }, _jsonOptions);
        }
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
            var request = context.Request;
            var response = context.Response;
            string? functionId = null;
            string? stepId = null;

            // Extract fnId and stepId from query parameters
            if (request.Query.TryGetValue("fnId", out var fnIdValues))
            {
                functionId = fnIdValues.FirstOrDefault();
            }

            if (request.Query.TryGetValue("stepId", out var stepIdValues))
            {
                stepId = stepIdValues.FirstOrDefault();
            }

            // Parse request body
            string requestBody = await new StreamReader(request.Body).ReadToEndAsync();
            var payload = JsonSerializer.Deserialize<CallRequestPayload>(requestBody, _jsonOptions);
                
            if (payload == null)
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsJsonAsync(new { error = "Invalid request payload" }, _jsonOptions);
                return;
            }

            // If use_api is true, we need to retrieve the full payload from Inngest server
            if (payload.Ctx.UseApi)
            {
                // Retrieve events from Inngest API
                var eventsUrl = $"{_apiOrigin}/v0/runs/{payload.Ctx.RunId}/batch";
                var eventsResponse = await _httpClient.GetAsync(eventsUrl);
                
                if (!eventsResponse.IsSuccessStatusCode)
                {
                    response.StatusCode = StatusCodes.Status500InternalServerError;
                    await response.WriteAsJsonAsync(new { error = "Failed to retrieve batch events from Inngest API" }, _jsonOptions);
                    return;
                }
                
                var eventsJson = await eventsResponse.Content.ReadAsStringAsync();
                var events = JsonSerializer.Deserialize<IEnumerable<InngestEvent>>(eventsJson, _jsonOptions);
                
                if (events != null && events.Any())
                {
                    payload.Events = events;
                    payload.Event = events.First();
                }
                
                // Retrieve memoized step data from Inngest API
                var actionsUrl = $"{_apiOrigin}/v0/runs/{payload.Ctx.RunId}/actions";
                var actionsResponse = await _httpClient.GetAsync(actionsUrl);
                
                if (!actionsResponse.IsSuccessStatusCode)
                {
                    response.StatusCode = StatusCodes.Status500InternalServerError;
                    await response.WriteAsJsonAsync(new { error = "Failed to retrieve step data from Inngest API" }, _jsonOptions);
                    return;
                }
                
                var actionsJson = await actionsResponse.Content.ReadAsStringAsync();
                var steps = JsonSerializer.Deserialize<Dictionary<string, object>>(actionsJson, _jsonOptions);
                
                if (steps != null)
                {
                    payload.Steps = steps;
                }
            }

            // Override functionId from query string if provided
            if (!string.IsNullOrEmpty(functionId))
            {
                payload.Ctx.FunctionId = functionId;
            }
            
            // Extract app-specific function ID (strip off the app prefix if present)
            string actualFunctionId = payload.Ctx.FunctionId;
            if (actualFunctionId.StartsWith($"{_appId}-"))
            {
                actualFunctionId = actualFunctionId.Substring(_appId.Length + 1);
            }

            var inngestContext = new InngestContext(
                payload.Event, 
                payload.Events, 
                payload.Steps,
                new CallRequestContext
                {
                    // Copy all properties from payload.Ctx, but override FunctionId
                    RunId = payload.Ctx.RunId,
                    FunctionId = actualFunctionId,
                    UseApi = payload.Ctx.UseApi,
                    // Add any other properties that CallRequestContext has
                },
                payload.Secrets ?? _secrets,
                this);

            if (_functions.TryGetValue(actualFunctionId, out var function))
            {
                try
                {
                    // If a specific step is requested, find and execute just that step
                    if (!string.IsNullOrEmpty(stepId) && stepId != "step")
                    {
                        // This is a specific step execution request
                        // In the future, implement step retrieval and execution based on stepId
                        
                        // For now, let's return 501 Not Implemented as step-specific execution is not yet supported
                        response.StatusCode = StatusCodes.Status501NotImplemented;
                        await response.WriteAsJsonAsync(new { 
                            error = "Step-specific execution is not yet implemented",
                            id = stepId,
                            op = "StepNotFound"
                        }, _jsonOptions);
                        return;
                    }
                    
                    // Otherwise execute the entire function
                    var result = await function.Handler(inngestContext);
                    
                    // Standard successful response with 200 OK
                    response.Headers["X-Inngest-Req-Version"] = "1";
                    await response.WriteAsJsonAsync(result, _jsonOptions);
                }
                catch (Exception ex)
                {
                    var errorResponse = new 
                    {
                        name = ex.GetType().Name,
                        message = ex.Message,
                        stack = ex.StackTrace
                    };
                    
                    // Determine if this is a retriable error
                    bool noRetry = ex is StepExecutionException && ((StepExecutionException)ex).NoRetry;
                    
                    if (noRetry)
                    {
                        response.StatusCode = StatusCodes.Status400BadRequest;
                        response.Headers["X-Inngest-No-Retry"] = "true";
                    }
                    else
                    {
                        response.StatusCode = StatusCodes.Status500InternalServerError;
                        response.Headers["X-Inngest-No-Retry"] = "false";
                        
                        // If retry delay was specified, include Retry-After header
                        if (ex is StepExecutionException retryEx && retryEx.RetryAfter.HasValue)
                        {
                            response.Headers["Retry-After"] = retryEx.RetryAfter.Value.TotalSeconds.ToString();
                        }
                    }
                    
                    await response.WriteAsJsonAsync(errorResponse, _jsonOptions);
                }
            }
            else
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                await response.WriteAsJsonAsync(new { error = $"Function '{actualFunctionId}' not found" }, _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new 
            { 
                name = ex.GetType().Name,
                message = ex.Message,
                stack = ex.StackTrace
            }, _jsonOptions);
        }
    }

    private async Task HandleIntrospectionRequest(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        // Check if the request has a signature and whether it's valid
        bool? authenticationSucceeded = null;
        if (request.Headers.ContainsKey("X-Inngest-Signature"))
        {
            authenticationSucceeded = await VerifySignature(context);
        }
        
        // Always include the unauthenticated response fields
        var responseObj = new Dictionary<string, object>
        {
            ["function_count"] = _functions.Count,
            ["has_event_key"] = !string.IsNullOrEmpty(_eventKey),
            ["has_signing_key"] = !string.IsNullOrEmpty(_signingKey),
            ["has_signing_key_fallback"] = !string.IsNullOrEmpty(_signingKeyFallback),
            ["mode"] = _isDev ? "dev" : "cloud",
            ["schema_version"] = "2024-05-24",
            ["authentication_succeeded"] = authenticationSucceeded
        };

        // Add authenticated fields if signature was verified
        if (authenticationSucceeded == true)
        {
            // Create SHA256 hash of keys if available
            string? eventKeyHash = null;
            string? signingKeyHash = null;
            string? signingKeyFallbackHash = null;
            
            if (!string.IsNullOrEmpty(_eventKey))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_eventKey));
                eventKeyHash = Convert.ToHexString(hashBytes).ToLower();
            }
            
            if (!string.IsNullOrEmpty(_signingKey))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_signingKey));
                signingKeyHash = Convert.ToHexString(hashBytes).ToLower();
            }
            
            if (!string.IsNullOrEmpty(_signingKeyFallback))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_signingKeyFallback));
                signingKeyFallbackHash = Convert.ToHexString(hashBytes).ToLower();
            }
            
            // Determine origin information
            string? serveOrigin = Environment.GetEnvironmentVariable("INNGEST_SERVE_ORIGIN");
            string? servePath = Environment.GetEnvironmentVariable("INNGEST_SERVE_PATH");
            
            // Add authenticated response fields
            responseObj["api_origin"] = _apiOrigin;
            responseObj["event_api_origin"] = _eventApiOrigin;
            responseObj["app_id"] = _appId;
            responseObj["env"] = _environment;
            responseObj["event_key_hash"] = eventKeyHash;
            responseObj["framework"] = "aspnetcore";
            responseObj["sdk_language"] = "dotnet";
            responseObj["sdk_version"] = _sdkVersion;
            responseObj["serve_origin"] = serveOrigin;
            responseObj["serve_path"] = servePath;
            responseObj["signing_key_hash"] = signingKeyHash;
            responseObj["signing_key_fallback_hash"] = signingKeyFallbackHash;
        }

        await response.WriteAsJsonAsync(responseObj, _jsonOptions);
    }
}