using System.Text.Json;

namespace Inngest.Steps;

/// <summary>
/// Delegate for sending events to Inngest
/// </summary>
/// <param name="events">Events to send</param>
/// <returns>Array of event IDs that were created</returns>
public delegate Task<string[]> SendEventsDelegate(InngestEvent[] events);

/// <summary>
/// Implementation of step tools for Inngest function execution.
///
/// This class handles the core step execution protocol:
/// 1. Check if step result is memoized in the steps dictionary
/// 2. If memoized: deserialize and return the cached result
/// 3. If not memoized: execute the step and throw StepInterruptException with the result
///
/// The StepInterruptException is caught by the handler which returns a 206 response
/// to Inngest with the step result. Inngest then calls the function again with the
/// result added to the steps dictionary.
/// </summary>
public class StepTools : IStepTools
{
    private readonly Dictionary<string, JsonElement> _memoizedSteps;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SendEventsDelegate? _sendEvents;

    /// <summary>
    /// Creates a new StepTools instance with memoized step results
    /// </summary>
    /// <param name="steps">Dictionary of step ID to result from Inngest</param>
    /// <param name="jsonOptions">JSON serialization options</param>
    /// <param name="sendEvents">Optional delegate to send events (required for SendEvent step)</param>
    public StepTools(Dictionary<string, object>? steps, JsonSerializerOptions jsonOptions, SendEventsDelegate? sendEvents = null)
    {
        _jsonOptions = jsonOptions;
        _sendEvents = sendEvents;

        // Convert the steps dictionary to JsonElement for type-safe deserialization
        _memoizedSteps = new Dictionary<string, JsonElement>();

        if (steps != null)
        {
            foreach (var kvp in steps)
            {
                if (kvp.Value is JsonElement je)
                {
                    _memoizedSteps[kvp.Key] = je;
                }
                else
                {
                    // Serialize and deserialize to get a JsonElement
                    var json = JsonSerializer.Serialize(kvp.Value, _jsonOptions);
                    _memoizedSteps[kvp.Key] = JsonSerializer.Deserialize<JsonElement>(json);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<T> Run<T>(string id, Func<Task<T>> handler, StepRunOptions? options = null)
    {
        // Check if step is already memoized
        if (_memoizedSteps.TryGetValue(id, out var memoized))
        {
            return DeserializeStepResult<T>(id, memoized);
        }

        // Step not memoized - execute it
        try
        {
            var result = await handler();

            // Throw interrupt with the result so Inngest can memoize it
            throw new StepInterruptException(new StepOperation
            {
                Id = id,
                Op = StepOpCode.StepRun,
                Data = result,
                DisplayName = options?.Name
            });
        }
        catch (StepInterruptException)
        {
            throw; // Re-throw step interrupts
        }
        catch (Exceptions.NonRetriableException)
        {
            throw; // Let non-retriable exceptions bubble up to be handled by InngestClient
        }
        catch (Exceptions.RetryAfterException)
        {
            throw; // Let retry-after exceptions bubble up to be handled by InngestClient
        }
        catch (Exception ex)
        {
            // Report step error to Inngest
            throw new StepInterruptException(new StepOperation
            {
                Id = id,
                Op = StepOpCode.StepError,
                Error = StepError.FromException(ex),
                DisplayName = options?.Name
            });
        }
    }

    /// <inheritdoc/>
    public Task<T> Run<T>(string id, Func<T> handler, StepRunOptions? options = null)
    {
        return Run(id, () => Task.FromResult(handler()), options);
    }

    /// <inheritdoc/>
    public async Task Run(string id, Func<Task> handler, StepRunOptions? options = null)
    {
        await Run<bool>(id, async () =>
        {
            await handler();
            return true;
        }, options);
    }

    /// <inheritdoc/>
    public Task Sleep(string id, string duration)
    {
        // Check if sleep has completed
        if (_memoizedSteps.ContainsKey(id))
        {
            return Task.CompletedTask;
        }

        // Request sleep from Inngest
        throw new StepInterruptException(new StepOperation
        {
            Id = id,
            Op = StepOpCode.Sleep,
            Opts = new SleepOpts { Duration = duration }
        });
    }

    /// <inheritdoc/>
    public Task Sleep(string id, TimeSpan duration)
    {
        return Sleep(id, FormatDuration(duration));
    }

    /// <inheritdoc/>
    public Task SleepUntil(string id, DateTimeOffset until)
    {
        if (_memoizedSteps.ContainsKey(id))
        {
            return Task.CompletedTask;
        }

        // Use ISO 8601 format for absolute time
        throw new StepInterruptException(new StepOperation
        {
            Id = id,
            Op = StepOpCode.Sleep,
            Opts = new SleepOpts { Duration = until.ToUniversalTime().ToString("O") }
        });
    }

    /// <inheritdoc/>
    public Task<T?> WaitForEvent<T>(string id, WaitForEventOptions options) where T : class
    {
        if (_memoizedSteps.TryGetValue(id, out var memoized))
        {
            // Check if timeout occurred (null result)
            if (memoized.ValueKind == JsonValueKind.Null)
            {
                return Task.FromResult<T?>(null);
            }

            // Handle V1/V2 executor format: { type: "data", data: <event> }
            var actualData = memoized;
            if (memoized.ValueKind == JsonValueKind.Object && memoized.TryGetProperty("data", out var dataElement))
            {
                // Check if data is null (timeout in V1/V2 format)
                if (dataElement.ValueKind == JsonValueKind.Null)
                {
                    return Task.FromResult<T?>(null);
                }
                actualData = dataElement;
            }

            return Task.FromResult(actualData.Deserialize<T>(_jsonOptions));
        }

        throw new StepInterruptException(new StepOperation
        {
            Id = id,
            Op = StepOpCode.WaitForEvent,
            Opts = new WaitForEventOpts
            {
                Event = options.Event,
                Timeout = options.Timeout,
                If = options.Match ?? options.If
            }
        });
    }

    /// <inheritdoc/>
    public Task<T?> Invoke<T>(string id, InvokeOptions options) where T : class
    {
        if (_memoizedSteps.TryGetValue(id, out var memoized))
        {
            // Check for error response from invoked function
            if (memoized.ValueKind == JsonValueKind.Object &&
                memoized.TryGetProperty("error", out var errorProp))
            {
                var errorMessage = errorProp.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString() ?? "Unknown error"
                    : "Invoked function failed";
                throw new InngestInvokeException(errorMessage);
            }

            // Check for data property (successful invocation)
            if (memoized.ValueKind == JsonValueKind.Object &&
                memoized.TryGetProperty("data", out var dataProp))
            {
                return Task.FromResult(dataProp.Deserialize<T>(_jsonOptions));
            }

            // Direct result
            if (memoized.ValueKind == JsonValueKind.Null)
            {
                return Task.FromResult<T?>(null);
            }

            return Task.FromResult(memoized.Deserialize<T>(_jsonOptions));
        }

        // Create event payload for the invoked function
        var payload = new
        {
            data = options.Data ?? new { },
            user = options.User
        };

        throw new StepInterruptException(new StepOperation
        {
            Id = id,
            Op = StepOpCode.InvokeFunction,
            Opts = new InvokeFunctionOpts
            {
                FunctionId = options.FunctionId,
                Payload = payload,
                Timeout = options.Timeout
            }
        });
    }

    /// <inheritdoc/>
    public async Task<string[]> SendEvent(string id, params InngestEvent[] events)
    {
        if (_memoizedSteps.TryGetValue(id, out var memoized))
        {
            // Inngest returns { ids: string[] } format or direct array
            return DeserializeSendEventResult(memoized);
        }

        // SendEvent is a "sync" step - we execute it immediately and return the result
        // This matches the TypeScript SDK behavior where sendEvent runs synchronously
        if (_sendEvents == null)
        {
            throw new InvalidOperationException(
                "SendEvent step requires an event sender delegate. " +
                "Ensure the InngestClient is properly configured.");
        }

        try
        {
            // Actually send the events to Inngest
            var eventIds = await _sendEvents(events);

            // Return the result as a StepRun operation (like step.run does)
            // This tells Inngest to memoize the result
            throw new StepInterruptException(new StepOperation
            {
                Id = id,
                Op = StepOpCode.StepRun,
                Name = "sendEvent",
                Data = new { ids = eventIds }
            });
        }
        catch (StepInterruptException)
        {
            throw; // Re-throw step interrupts
        }
        catch (Exceptions.NonRetriableException)
        {
            throw; // Let non-retriable exceptions bubble up to be handled by InngestClient
        }
        catch (Exceptions.RetryAfterException)
        {
            throw; // Let retry-after exceptions bubble up to be handled by InngestClient
        }
        catch (Exception ex)
        {
            // Report step error to Inngest
            throw new StepInterruptException(new StepOperation
            {
                Id = id,
                Op = StepOpCode.StepError,
                Name = "sendEvent",
                Error = StepError.FromException(ex)
            });
        }
    }

    /// <summary>
    /// Deserialize the SendEvent memoized result which comes as { ids: string[] }
    /// </summary>
    private string[] DeserializeSendEventResult(JsonElement element)
    {
        // Inngest returns { ids: [...] } format
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("ids", out var idsElement))
        {
            return idsElement.Deserialize<string[]>(_jsonOptions) ?? Array.Empty<string>();
        }

        // Fallback: try to deserialize directly as string[] for backward compatibility
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.Deserialize<string[]>(_jsonOptions) ?? Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Deserialize a memoized step result to the expected type
    /// </summary>
    private T DeserializeStepResult<T>(string stepId, JsonElement element)
    {
        try
        {
            // Inngest sends step results wrapped as {"data": <value>}
            // Extract the actual data from the wrapper if present
            var actualData = element;
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var dataElement))
            {
                actualData = dataElement;
            }

            // Handle null
            if (actualData.ValueKind == JsonValueKind.Null)
            {
                return default!;
            }

            // Handle primitive types
            if (typeof(T) == typeof(string) && actualData.ValueKind == JsonValueKind.String)
            {
                return (T)(object)actualData.GetString()!;
            }

            if (typeof(T) == typeof(int) && actualData.ValueKind == JsonValueKind.Number)
            {
                return (T)(object)actualData.GetInt32();
            }

            if (typeof(T) == typeof(long) && actualData.ValueKind == JsonValueKind.Number)
            {
                return (T)(object)actualData.GetInt64();
            }

            if (typeof(T) == typeof(double) && actualData.ValueKind == JsonValueKind.Number)
            {
                return (T)(object)actualData.GetDouble();
            }

            if (typeof(T) == typeof(bool) && (actualData.ValueKind == JsonValueKind.True || actualData.ValueKind == JsonValueKind.False))
            {
                return (T)(object)actualData.GetBoolean();
            }

            // Deserialize complex types
            var result = actualData.Deserialize<T>(_jsonOptions);
            if (result == null)
            {
                throw new InvalidOperationException($"Failed to deserialize step '{stepId}' result to {typeof(T).Name}");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize step '{stepId}' result to {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Format a TimeSpan as a human-readable duration string
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        // Build duration string from largest to smallest units
        var parts = new List<string>();

        if (duration.Days > 0)
        {
            parts.Add($"{duration.Days}d");
            duration = duration.Subtract(TimeSpan.FromDays(duration.Days));
        }

        if (duration.Hours > 0)
        {
            parts.Add($"{duration.Hours}h");
            duration = duration.Subtract(TimeSpan.FromHours(duration.Hours));
        }

        if (duration.Minutes > 0)
        {
            parts.Add($"{duration.Minutes}m");
            duration = duration.Subtract(TimeSpan.FromMinutes(duration.Minutes));
        }

        if (duration.Seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{duration.Seconds}s");
        }

        return string.Join("", parts);
    }
}

/// <summary>
/// Exception thrown when an invoked function fails
/// </summary>
public class InngestInvokeException : Exception
{
    /// <summary>
    /// Creates a new InngestInvokeException
    /// </summary>
    public InngestInvokeException(string message) : base(message) { }

    /// <summary>
    /// Creates a new InngestInvokeException with inner exception
    /// </summary>
    public InngestInvokeException(string message, Exception innerException)
        : base(message, innerException) { }
}
