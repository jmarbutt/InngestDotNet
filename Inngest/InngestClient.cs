using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inngest.Configuration;
using Inngest.Exceptions;
using Inngest.Internal;
using Inngest.Steps;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly string _sdkVersion = "1.2.1";
    private readonly string _appId;
    private readonly ILogger _logger;
    private readonly IInngestFunctionRegistry? _registry;
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initialize a new Inngest client
    /// </summary>
    /// <param name="eventKey">Your Inngest event key (defaults to INNGEST_EVENT_KEY environment variable)</param>
    /// <param name="signingKey">Your Inngest signing key (defaults to INNGEST_SIGNING_KEY environment variable)</param>
    /// <param name="apiOrigin">Custom API origin (defaults to environment or Inngest Cloud)</param>
    /// <param name="eventApiOrigin">Custom event API origin (defaults to environment or Inngest Cloud)</param>
    /// <param name="httpClient">Optional custom HttpClient</param>
    /// <param name="appId">Application ID for identifying this app in Inngest (defaults to "inngest-dotnet")</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    public InngestClient(
        string? eventKey = null,
        string? signingKey = null,
        string? apiOrigin = null,
        string? eventApiOrigin = null,
        HttpClient? httpClient = null,
        string? appId = null,
        ILogger? logger = null)
    {
        // Check environment variables according to SDK spec
        _eventKey = eventKey ?? Environment.GetEnvironmentVariable("INNGEST_EVENT_KEY") ?? "";
        _signingKey = signingKey ?? Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY") ?? "";
        _signingKeyFallback = Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY_FALLBACK");
        _environment = Environment.GetEnvironmentVariable("INNGEST_ENV") ?? "dev";
        _isDev = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INNGEST_DEV"));
        _appId = appId ?? Environment.GetEnvironmentVariable("INNGEST_APP_ID") ?? "inngest-dotnet";
        _logger = logger ?? NullLogger.Instance;

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
    /// Initialize a new Inngest client with options pattern
    /// </summary>
    /// <param name="options">Configuration options</param>
    /// <param name="registry">Function registry for attribute-based functions</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="httpClient">Optional custom HttpClient</param>
    /// <param name="logger">Optional logger for diagnostics</param>
    internal InngestClient(
        InngestOptions options,
        IInngestFunctionRegistry? registry = null,
        IServiceProvider? serviceProvider = null,
        HttpClient? httpClient = null,
        ILogger<InngestClient>? logger = null)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<InngestClient>.Instance;

        // Apply configuration from options
        _eventKey = options.EventKey ?? "";
        _signingKey = options.SigningKey ?? "";
        _signingKeyFallback = options.SigningKeyFallback;
        _environment = options.Environment ?? "dev";
        _isDev = options.IsDev ?? false;
        _appId = options.AppId ?? "inngest-app";

        // Set API endpoints
        var devServerUrl = options.DevServerUrl ?? "http://localhost:8288";

        _apiOrigin = options.ApiOrigin ??
                     (_isDev ? devServerUrl : "https://api.inngest.com");

        _eventApiOrigin = options.EventApiOrigin ??
                          (_isDev ? devServerUrl : "https://inn.gs");

        _httpClient = httpClient ?? new HttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Register functions from the registry
        if (_registry != null)
        {
            RegisterFunctionsFromRegistry();
        }
    }

    private void RegisterFunctionsFromRegistry()
    {
        if (_registry == null || _serviceProvider == null) return;

        foreach (var registration in _registry.GetRegistrations())
        {
            var handler = FunctionAdapter.CreateHandler(registration, _serviceProvider);
            var functionDefinition = new FunctionDefinition(
                registration.Id,
                registration.Name,
                registration.Triggers,
                handler,
                registration.Options
            );

            var fullId = $"{_appId}-{registration.Id}";
            _functions[registration.Id] = functionDefinition;
        }
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
            _logger.LogDebug("Skipping signature verification in dev mode");
            return true;
        }

        var request = context.Request;

        // Check if we have a signing key
        if (string.IsNullOrEmpty(_signingKey) && string.IsNullOrEmpty(_signingKeyFallback))
        {
            _logger.LogWarning("Signature verification failed: No signing key configured");
            return false;
        }

        // Get the signature from the header
        if (!request.Headers.TryGetValue("X-Inngest-Signature", out var signatureHeader))
        {
            _logger.LogWarning("Signature verification failed: X-Inngest-Signature header missing");
            return false;
        }

        string signature = signatureHeader.ToString();
        _logger.LogDebug("Received signature header: {SignatureHeader}", signature);

        // Parse the signature components (t=timestamp&s=signature)
        var components = System.Web.HttpUtility.ParseQueryString(signature);
        if (!long.TryParse(components["t"], out long timestamp))
        {
            _logger.LogWarning("Signature verification failed: Unable to parse timestamp from signature");
            return false;
        }

        string signatureValue = components["s"] ?? "";
        if (string.IsNullOrEmpty(signatureValue))
        {
            _logger.LogWarning("Signature verification failed: No signature value in header");
            return false;
        }

        // Verify timestamp is not too old (within last 5 minutes)
        var timestampDateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var timeDelta = DateTimeOffset.UtcNow.Subtract(timestampDateTime);
        if (timeDelta.TotalMinutes > 5)
        {
            _logger.LogWarning("Signature verification failed: Timestamp too old ({TimeDelta:F1} minutes)", timeDelta.TotalMinutes);
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
        // The data to sign is: body + timestamp (as string)
        var dataToSign = requestBody + timestamp;
        bool verified = VerifyHmacSha256(dataToSign, _signingKey, signatureValue);

        // If primary fails and we have a fallback, try that
        if (!verified && !string.IsNullOrEmpty(_signingKeyFallback))
        {
            _logger.LogDebug("Primary signing key verification failed, trying fallback key");
            verified = VerifyHmacSha256(dataToSign, _signingKeyFallback, signatureValue);
        }

        if (!verified)
        {
            _logger.LogWarning("Signature verification failed: HMAC mismatch (key prefix: {KeyPrefix})",
                SigningKeyPrefixRegex.Match(_signingKey).Value);
        }
        else
        {
            _logger.LogDebug("Signature verification succeeded");
        }

        return verified;
    }
    
    /// <summary>
    /// Regex pattern to match and remove the signkey prefix (e.g., "signkey-prod-", "signkey-test-")
    /// The format is: signkey-{env}-{actual_key}
    /// </summary>
    private static readonly Regex SigningKeyPrefixRegex = new(@"^signkey-\w+-", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a signing key by removing the signkey-{env}- prefix.
    /// The Inngest signing key format is: signkey-{env}-{actual_key_hex}
    /// For HMAC verification, only the actual key portion should be used.
    /// </summary>
    private static string NormalizeSigningKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;

        return SigningKeyPrefixRegex.Replace(key, "");
    }

    private bool VerifyHmacSha256(string data, string key, string expectedSignature)
    {
        // Normalize the key by removing the signkey-{env}- prefix
        var normalizedKey = NormalizeSigningKey(key);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(normalizedKey));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        string computedSignature = Convert.ToHexString(hashBytes).ToLower();

        // Use constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(expectedSignature.ToLower()));
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

        try
        {
            // Check for sync kind - in-band vs out-of-band
            var syncKind = request.Headers.TryGetValue("X-Inngest-Sync-Kind", out var syncKindValues)
                ? syncKindValues.FirstOrDefault()
                : null;
            var useInBandSync = string.Equals(syncKind, "inband", StringComparison.OrdinalIgnoreCase);

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

            // In dev mode, prefer http to avoid HTTPS issues
            var scheme = _isDev ? "http" : (request.Headers.ContainsKey("X-Forwarded-Proto")
                ? request.Headers["X-Forwarded-Proto"].ToString()
                : request.Scheme);

            // If host doesn't include a protocol, add it
            if (!host.StartsWith("http://") && !host.StartsWith("https://"))
            {
                host = $"{scheme}://{host}";
            }

            // Use the full request path if servePath is not specified
            // PathBase contains the mapped path when using app.Map()
            var path = string.IsNullOrEmpty(servePath)
                ? request.PathBase.ToString()
                : servePath;

            // Ensure path starts with a slash if it's not empty
            if (!string.IsNullOrEmpty(path) && !path.StartsWith("/"))
            {
                path = $"/{path}";
            }

            url = $"{host}{path}";

            _logger.LogDebug("URL construction: host={Host}, pathBase={PathBase}, path={Path}", host, request.PathBase, path);
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
                    var triggerObj = new Dictionary<string, object>();
                    triggerObj["event"] = trigger.Event;

                    if (trigger.Constraint?.Expression != null)
                    {
                        triggerObj["expression"] = trigger.Constraint.Expression;
                    }

                    triggers.Add(triggerObj);
                }
            }

            // Build step definitions - use registered steps if any, otherwise default single step
            var stepsDict = new Dictionary<string, object>();
            if (fn.Steps.Count > 0)
            {
                foreach (var step in fn.Steps)
                {
                    var stepObj = new Dictionary<string, object>
                    {
                        ["id"] = step.Id,
                        ["name"] = step.Name ?? step.Id,
                        ["runtime"] = new
                        {
                            type = "http",
                            url = $"{url.TrimEnd('/')}?stepId={step.Id}&fnId={fn.Id}"
                        }
                    };

                    if (step.RetryOptions != null)
                    {
                        stepObj["retries"] = BuildStepRetryConfig(step.RetryOptions);
                    }

                    stepsDict[step.Id] = stepObj;
                }
            }
            else
            {
                // Default single step for functions without explicit step definitions
                var defaultStep = new Dictionary<string, object>
                {
                    ["id"] = "step",
                    ["name"] = "step",
                    ["runtime"] = new
                    {
                        type = "http",
                        url = $"{url.TrimEnd('/')}?stepId=step&fnId={fn.Id}"
                    }
                };

                if (fn.Options?.Retry != null)
                {
                    defaultStep["retries"] = BuildStepRetryConfig(fn.Options.Retry);
                }

                stepsDict["step"] = defaultStep;
            }

            // Build the function object with all configuration options
            var functionObj = new Dictionary<string, object>
            {
                ["id"] = $"{_appId}-{fn.Id}",
                ["name"] = fn.Name,
                ["triggers"] = triggers,
                ["steps"] = stepsDict
            };

            // Add optional configuration
            if (fn.Options != null)
            {
                // Concurrency
                if (fn.Options.ConcurrencyOptions != null)
                {
                    var concurrency = new Dictionary<string, object>
                    {
                        ["limit"] = fn.Options.ConcurrencyOptions.Limit
                    };
                    if (fn.Options.ConcurrencyOptions.Key != null)
                        concurrency["key"] = fn.Options.ConcurrencyOptions.Key;
                    if (fn.Options.ConcurrencyOptions.Scope != null)
                        concurrency["scope"] = fn.Options.ConcurrencyOptions.Scope;
                    functionObj["concurrency"] = new[] { concurrency };
                }
                else if (fn.Options.Concurrency.HasValue)
                {
                    functionObj["concurrency"] = new[] { new { limit = fn.Options.Concurrency.Value } };
                }

                // Rate limit
                if (fn.Options.RateLimit != null)
                {
                    var rateLimit = new Dictionary<string, object>
                    {
                        ["limit"] = fn.Options.RateLimit.Limit,
                        ["period"] = fn.Options.RateLimit.Period
                    };
                    if (fn.Options.RateLimit.Key != null)
                        rateLimit["key"] = fn.Options.RateLimit.Key;
                    functionObj["rateLimit"] = rateLimit;
                }

                // Throttle
                if (fn.Options.Throttle != null)
                {
                    var throttle = new Dictionary<string, object>
                    {
                        ["limit"] = fn.Options.Throttle.Limit,
                        ["period"] = fn.Options.Throttle.Period
                    };
                    if (fn.Options.Throttle.Key != null)
                        throttle["key"] = fn.Options.Throttle.Key;
                    if (fn.Options.Throttle.Burst.HasValue)
                        throttle["burst"] = fn.Options.Throttle.Burst.Value;
                    functionObj["throttle"] = throttle;
                }

                // Debounce
                if (fn.Options.Debounce != null)
                {
                    var debounce = new Dictionary<string, object>
                    {
                        ["period"] = fn.Options.Debounce.Period
                    };
                    if (fn.Options.Debounce.Key != null)
                        debounce["key"] = fn.Options.Debounce.Key;
                    if (fn.Options.Debounce.Timeout != null)
                        debounce["timeout"] = fn.Options.Debounce.Timeout;
                    functionObj["debounce"] = debounce;
                }

                // Batch (event batching)
                if (fn.Options.Batch != null)
                {
                    var batchEvents = new Dictionary<string, object>
                    {
                        ["maxSize"] = fn.Options.Batch.MaxSize
                    };
                    if (fn.Options.Batch.Timeout != null)
                        batchEvents["timeout"] = fn.Options.Batch.Timeout;
                    if (fn.Options.Batch.Key != null)
                        batchEvents["key"] = fn.Options.Batch.Key;
                    functionObj["batchEvents"] = batchEvents;
                }

                // Priority
                if (fn.Options.Priority.HasValue)
                {
                    functionObj["priority"] = new { run = fn.Options.Priority.Value };
                }

                // Cancellation
                if (fn.Options.Cancellation != null)
                {
                    var cancel = new Dictionary<string, object>
                    {
                        ["event"] = fn.Options.Cancellation.Event
                    };
                    if (fn.Options.Cancellation.Match != null)
                        cancel["match"] = fn.Options.Cancellation.Match;
                    if (fn.Options.Cancellation.If != null)
                        cancel["if"] = fn.Options.Cancellation.If;
                    if (fn.Options.Cancellation.Timeout != null)
                        cancel["timeout"] = fn.Options.Cancellation.Timeout;
                    functionObj["cancel"] = new[] { cancel };
                }

                // Idempotency
                if (fn.Options.IdempotencyKey != null)
                {
                    functionObj["idempotency"] = fn.Options.IdempotencyKey;
                }

                // Retries at function level - Inngest expects just an integer
                if (fn.Options.Retry != null && fn.Options.Retry.Attempts.HasValue)
                {
                    functionObj["retries"] = fn.Options.Retry.Attempts.Value;
                }
            }

            fnArray.Add(functionObj);
        }

        // Handle in-band sync - return all data directly in the response
        if (useInBandSync)
        {
            _logger.LogInformation("In-band sync: returning {FunctionCount} functions with URL: {Url}", fnArray.Count, url);

            // Build capabilities
            var capabilities = new Dictionary<string, string>
            {
                ["in_band_sync"] = "v1"
            };

            // Build inspection data
            var inspection = new Dictionary<string, object?>
            {
                ["api_origin"] = _apiOrigin,
                ["app_id"] = _appId,
                ["authentication_succeeded"] = true,
                ["capabilities"] = capabilities,
                ["env"] = _environment,
                ["event_api_origin"] = _eventApiOrigin,
                ["framework"] = "aspnetcore",
                ["has_event_key"] = !string.IsNullOrEmpty(_eventKey),
                ["has_signing_key"] = !string.IsNullOrEmpty(_signingKey),
                ["has_signing_key_fallback"] = !string.IsNullOrEmpty(_signingKeyFallback),
                ["mode"] = _isDev ? "dev" : "cloud",
                ["sdk_language"] = "csharp",
                ["sdk_version"] = _sdkVersion,
                ["serve_origin"] = serveOrigin,
                ["serve_path"] = servePath
            };

            // Hash keys for inspection if present
            if (!string.IsNullOrEmpty(_eventKey))
            {
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_eventKey));
                inspection["event_key_hash"] = Convert.ToHexString(hashBytes).ToLower()[..16];
            }
            if (!string.IsNullOrEmpty(_signingKey))
            {
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_signingKey));
                inspection["signing_key_hash"] = Convert.ToHexString(hashBytes).ToLower()[..16];
            }
            if (!string.IsNullOrEmpty(_signingKeyFallback))
            {
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(_signingKeyFallback));
                inspection["signing_key_fallback_hash"] = Convert.ToHexString(hashBytes).ToLower()[..16];
            }

            // Build the in-band sync response
            var inBandResponse = new Dictionary<string, object?>
            {
                ["app_id"] = _appId,
                ["env"] = _isDev ? null : _environment,
                ["framework"] = "aspnetcore",
                ["functions"] = fnArray,
                ["inspection"] = inspection,
                ["platform"] = null,
                ["sdk_author"] = "inngest",
                ["sdk_language"] = "csharp",
                ["sdk_version"] = _sdkVersion,
                ["url"] = url
            };

            // Set response headers
            response.Headers["X-Inngest-Sync-Kind"] = "inband";
            response.Headers["Content-Type"] = "application/json";

            // Sign the response if not in dev mode
            if (!_isDev && !string.IsNullOrEmpty(_signingKey))
            {
                var responseJson = JsonSerializer.Serialize(inBandResponse, _jsonOptions);
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var dataToSign = $"{responseJson}{timestamp}";

                var normalizedKey = NormalizeSigningKey(_signingKey);
                var keyBytes = Convert.FromHexString(normalizedKey);
                using var hmac = new HMACSHA256(keyBytes);
                var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
                var signature = Convert.ToHexString(signatureBytes).ToLower();

                response.Headers["X-Inngest-Signature"] = $"t={timestamp}&s={signature}";
            }

            response.StatusCode = StatusCodes.Status200OK;
            await response.WriteAsJsonAsync(inBandResponse, _jsonOptions);
            return;
        }

        // Out-of-band sync - POST to Inngest API
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
        _logger.LogInformation("Out-of-band sync: Registering {FunctionCount} functions with URL: {Url}", fnArray.Count, url);
        _logger.LogDebug("Sending registration to: {ApiOrigin}/fn/register", _apiOrigin);

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
            // Create a bearer token from the hashed signing key as per SDK spec
            // The key material (after prefix) must be treated as hex and hashed with SHA256
            // See: https://github.com/inngest/inngest/blob/main/docs/SDK_SPEC.md
            var prefixMatch = SigningKeyPrefixRegex.Match(_signingKey);
            var prefix = prefixMatch.Success ? prefixMatch.Value.TrimEnd('-') : "signkey-prod";
            var normalizedKey = NormalizeSigningKey(_signingKey);

            using var sha256 = SHA256.Create();
            var keyBytes = Convert.FromHexString(normalizedKey);  // Treat as hex, not UTF-8
            var hashBytes = sha256.ComputeHash(keyBytes);
            var hashedKey = $"{prefix}-{Convert.ToHexString(hashBytes).ToLower()}";
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
            _logger.LogError("Registration failed with status {StatusCode}: {ErrorContent}", registerResponse.StatusCode, errorContent);
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new
            {
                error = "internal_server_error",
                message = $"Sync failed: {errorContent}",
                modified = false
            }, _jsonOptions);
        }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during sync");
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsJsonAsync(new
            {
                error = "internal_server_error",
                message = $"Sync exception: {ex.Message}",
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
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Extract fnId from query parameters
            string? functionId = null;
            if (request.Query.TryGetValue("fnId", out var fnIdValues))
            {
                functionId = fnIdValues.FirstOrDefault();
            }

            // Parse request body as CallRequestPayload
            request.EnableBuffering();
            await using var bodyStream = new MemoryStream();
            await request.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;
            request.Body.Position = 0;

            var payload = await JsonSerializer.DeserializeAsync<CallRequestPayload>(bodyStream, _jsonOptions);
            if (payload == null || payload.Event == null)
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsJsonAsync(new { error = "Invalid request payload" }, _jsonOptions);
                return;
            }

            // Get the first event
            var firstEvent = payload.Event;

            // Extract function ID from query parameter or event data
            string? fnId = functionId;
            if (string.IsNullOrEmpty(fnId) && firstEvent.Data is JsonElement dataElement &&
                dataElement.TryGetProperty("_inngest", out var inngestElement) &&
                inngestElement.TryGetProperty("fn_id", out var fnIdElement) &&
                fnIdElement.ValueKind == JsonValueKind.String)
            {
                fnId = fnIdElement.GetString();
            }

            // Validate function ID
            if (string.IsNullOrEmpty(fnId))
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsJsonAsync(new { error = "Function ID is required" }, _jsonOptions);
                return;
            }

            // Extract app-specific function ID (strip off the app prefix if present)
            string actualFunctionId = fnId;
            if (actualFunctionId.StartsWith($"{_appId}-"))
            {
                actualFunctionId = actualFunctionId.Substring(_appId.Length + 1);
            }

            // Find the function
            if (!_functions.TryGetValue(actualFunctionId, out var function))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                await response.WriteAsJsonAsync(new { error = $"Function '{actualFunctionId}' not found" }, _jsonOptions);
                return;
            }

            // Ensure the payload has all required fields
            payload.Steps ??= new Dictionary<string, object>();
            payload.Events ??= new List<InngestEvent> { firstEvent };

            string runId = payload.Ctx?.RunId ?? firstEvent.Id ?? Guid.NewGuid().ToString();
            int attempt = payload.Ctx?.Attempt ?? 0;

            // Create step tools with memoized state from Inngest
            var stepTools = new StepTools(payload.Steps, _jsonOptions);

            // Create the execution context
            var inngestContext = new InngestContext(
                firstEvent,
                payload.Events,
                stepTools,
                new RunContext
                {
                    RunId = runId,
                    FunctionId = actualFunctionId,
                    Attempt = attempt,
                    IsReplay = payload.Steps.Count > 0
                },
                _logger);

            _logger.LogDebug("Executing function {FunctionId} (run: {RunId}, attempt: {Attempt}, memoized steps: {StepCount})",
                actualFunctionId, runId, attempt, payload.Steps.Count);

            try
            {
                // Execute the function
                var result = await function.Handler(inngestContext);

                // Function completed successfully - return 200 with result
                _logger.LogDebug("Function {FunctionId} completed successfully", actualFunctionId);
                response.StatusCode = StatusCodes.Status200OK;
                await response.WriteAsJsonAsync(result, _jsonOptions);
            }
            catch (StepInterruptException stepEx)
            {
                // Steps need to be scheduled - return 206 with operations
                _logger.LogDebug("Function {FunctionId} requires step scheduling: {StepCount} operation(s)",
                    actualFunctionId, stepEx.Operations.Count);

                response.StatusCode = 206; // Partial Content
                await response.WriteAsJsonAsync(stepEx.Operations, _jsonOptions);
            }
            catch (NonRetriableException ex)
            {
                // Non-retriable error - return 400 with no-retry header
                _logger.LogWarning(ex, "Function {FunctionId} failed with non-retriable error", actualFunctionId);

                response.StatusCode = StatusCodes.Status400BadRequest;
                response.Headers["X-Inngest-No-Retry"] = "true";
                await WriteErrorResponse(response, ex);
            }
            catch (RetryAfterException ex)
            {
                // Retriable error with specific delay
                _logger.LogWarning(ex, "Function {FunctionId} failed, retry after {RetryAfter}", actualFunctionId, ex.RetryAfter);

                response.StatusCode = StatusCodes.Status500InternalServerError;
                response.Headers["X-Inngest-No-Retry"] = "false";
                response.Headers["Retry-After"] = ((int)ex.RetryAfter.TotalSeconds).ToString();
                await WriteErrorResponse(response, ex);
            }
            catch (Exception ex)
            {
                // Retriable error
                _logger.LogError(ex, "Function {FunctionId} failed with retriable error", actualFunctionId);

                response.StatusCode = StatusCodes.Status500InternalServerError;
                response.Headers["X-Inngest-No-Retry"] = "false";
                await WriteErrorResponse(response, ex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle call request");
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteErrorResponse(response, ex);
        }
    }

    private async Task WriteErrorResponse(HttpResponse response, Exception ex)
    {
        var errorResponse = new
        {
            name = ex.GetType().Name,
            message = ex.Message,
            stack = ex.StackTrace
        };
        await response.WriteAsJsonAsync(errorResponse, _jsonOptions);
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

    /// <summary>
    /// Builds a step-level retry configuration object for the sync payload
    /// Step retries use an object format with 'attempts' field
    /// </summary>
    private static object BuildStepRetryConfig(RetryOptions retry)
    {
        var config = new Dictionary<string, object>();

        if (retry.Attempts.HasValue)
            config["attempts"] = retry.Attempts.Value;

        return config;
    }
}