using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Inngest.Steps;

namespace Inngest.Internal;

/// <summary>
/// Adapts attribute-based IInngestFunction classes to the delegate handler format
/// used by the existing execution machinery
/// </summary>
internal static class FunctionAdapter
{
    /// <summary>
    /// Creates a handler delegate for a function registration
    /// </summary>
    public static Func<InngestContext, Task<object>> CreateHandler(
        FunctionRegistration registration,
        IServiceProvider serviceProvider)
    {
        return async (context) =>
        {
            // Create a new scope for each invocation to support scoped services
            using var scope = serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            // Resolve the function instance through DI
            var function = scopedProvider.GetRequiredService(registration.FunctionType);

            // Get cancellation token from context (or default)
            var ct = context.CancellationToken;

            object? result;

            if (registration.EventDataType != null)
            {
                // Typed handler - create typed context and invoke
                result = await InvokeTypedHandler(function, registration, context, ct);
            }
            else
            {
                // Untyped handler - invoke directly
                var inngestFunction = (IInngestFunction)function;
                result = await inngestFunction.ExecuteAsync(context, ct);
            }

            return result ?? new { };
        };
    }

    private static async Task<object?> InvokeTypedHandler(
        object function,
        FunctionRegistration registration,
        InngestContext context,
        CancellationToken ct)
    {
        // Get the ExecuteAsync method
        var executeMethod = registration.FunctionType.GetMethod("ExecuteAsync");
        if (executeMethod == null)
        {
            throw new InvalidOperationException(
                $"Function type {registration.FunctionType.Name} does not have an ExecuteAsync method");
        }

        // Create typed context
        var typedContext = CreateTypedContext(context, registration.EventDataType!);

        // Invoke the method
        var task = (Task<object?>?)executeMethod.Invoke(function, new object[] { typedContext, ct });
        if (task == null)
        {
            throw new InvalidOperationException(
                $"ExecuteAsync on {registration.FunctionType.Name} returned null");
        }

        return await task;
    }

    private static object CreateTypedContext(InngestContext context, Type eventDataType)
    {
        // Use reflection to create InngestContext<TEventData>
        var typedContextType = typeof(InngestContext<>).MakeGenericType(eventDataType);

        // Deserialize the event data to the target type
        var typedEventData = DeserializeEventData(context.Event.Data, eventDataType);

        // Create typed event
        var typedEventType = typeof(InngestEvent<>).MakeGenericType(eventDataType);
        var typedEvent = Activator.CreateInstance(typedEventType);

        // Copy properties from original event - use base type for inherited properties
        var originalEvent = context.Event;
        var baseEventType = typeof(InngestEvent);

        baseEventType.GetProperty("Id")?.SetValue(typedEvent, originalEvent.Id);
        baseEventType.GetProperty("Name")?.SetValue(typedEvent, originalEvent.Name);
        baseEventType.GetProperty("Timestamp")?.SetValue(typedEvent, originalEvent.Timestamp);
        baseEventType.GetProperty("User")?.SetValue(typedEvent, originalEvent.User);

        // Set Data using the typed property (declared on the generic type with 'new' keyword)
        var dataProperty = typedEventType.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        dataProperty?.SetValue(typedEvent, typedEventData);

        // Create typed context using internal constructor
        var typedContext = Activator.CreateInstance(
            typedContextType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new object?[]
            {
                typedEvent,
                context.Events,
                context.Step,
                context.Run,
                context.Logger,
                context.CancellationToken
            },
            null);

        return typedContext ?? throw new InvalidOperationException("Failed to create typed context");
    }

    private static object? DeserializeEventData(object? data, Type targetType)
    {
        if (data == null)
            return null;

        if (data.GetType() == targetType)
            return data;

        if (data is JsonElement jsonElement)
        {
            return jsonElement.Deserialize(targetType, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }

        // Try to serialize and deserialize as a fallback
        var json = JsonSerializer.Serialize(data);
        return JsonSerializer.Deserialize(json, targetType, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>
    /// Creates a handler delegate for a failure handler
    /// </summary>
    public static Func<InngestContext, Task<object>> CreateFailureHandler(
        Type failureHandlerType,
        string parentFunctionId,
        IServiceProvider serviceProvider)
    {
        return async (context) =>
        {
            // Create a new scope for each invocation to support scoped services
            using var scope = serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            // Resolve the failure handler instance through DI
            var handler = (IInngestFailureHandler)scopedProvider.GetRequiredService(failureHandlerType);

            // Parse the inngest/function.failed event data
            var (failureInfo, originalEvent) = ParseFailureEvent(context.Event);

            // Verify this failure is for our parent function
            if (failureInfo.FunctionId != parentFunctionId)
            {
                // This shouldn't happen if the trigger filter is set correctly,
                // but guard against it anyway
                return new { skipped = true, reason = "function_id_mismatch" };
            }

            // Create the failure context
            var failureContext = new FailureContext(
                failureInfo,
                originalEvent,
                context.Step,
                context.Run,
                context.Logger,
                context.CancellationToken);

            // Execute the failure handler
            await handler.HandleFailureAsync(failureContext, context.CancellationToken);

            return new { handled = true };
        };
    }

    /// <summary>
    /// Parse the inngest/function.failed event into a FunctionFailureInfo
    /// </summary>
    private static (FunctionFailureInfo Info, InngestEvent OriginalEvent) ParseFailureEvent(InngestEvent failedEvent)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // The event data contains: function_id, run_id, error, event (original)
        JsonElement data;
        if (failedEvent.Data is JsonElement je)
        {
            data = je;
        }
        else if (failedEvent.Data != null)
        {
            var json = JsonSerializer.Serialize(failedEvent.Data, jsonOptions);
            data = JsonSerializer.Deserialize<JsonElement>(json, jsonOptions);
        }
        else
        {
            throw new InvalidOperationException("inngest/function.failed event has no data");
        }

        var functionId = data.TryGetProperty("function_id", out var fidProp)
            ? fidProp.GetString() ?? ""
            : "";

        var runId = data.TryGetProperty("run_id", out var ridProp)
            ? ridProp.GetString() ?? ""
            : "";

        FunctionError error;
        if (data.TryGetProperty("error", out var errorProp))
        {
            error = new FunctionError
            {
                Name = errorProp.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? "Error"
                    : "Error",
                Message = errorProp.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString() ?? ""
                    : "",
                Stack = errorProp.TryGetProperty("stack", out var stackProp)
                    ? stackProp.GetString()
                    : null
            };
        }
        else
        {
            error = new FunctionError { Message = "Unknown error" };
        }

        // Parse the original event
        InngestEvent originalEvent;
        if (data.TryGetProperty("event", out var eventProp))
        {
            originalEvent = eventProp.Deserialize<InngestEvent>(jsonOptions) ?? new InngestEvent();
        }
        else
        {
            originalEvent = new InngestEvent();
        }

        var info = new FunctionFailureInfo
        {
            FunctionId = functionId,
            RunId = runId,
            Error = error
        };

        return (info, originalEvent);
    }

    // Extension class to include OriginalEvent in FunctionFailureInfo
    private record struct ParsedFailureInfo(FunctionFailureInfo Info, InngestEvent OriginalEvent);
}
