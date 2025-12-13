using System.Reflection;
using Inngest.Attributes;

namespace Inngest.Internal;

/// <summary>
/// Stores information about a registered function
/// </summary>
internal sealed class FunctionRegistration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Type FunctionType { get; init; }
    public required FunctionTrigger[] Triggers { get; init; }
    public FunctionOptions? Options { get; init; }
    public Type? EventDataType { get; init; }
}

/// <summary>
/// Registry for attribute-based Inngest functions
/// </summary>
internal interface IInngestFunctionRegistry
{
    /// <summary>
    /// Registers a function type
    /// </summary>
    void RegisterFunction(Type functionType);

    /// <summary>
    /// Scans an assembly for Inngest functions and registers them
    /// </summary>
    void RegisterFunctionsFromAssembly(Assembly assembly);

    /// <summary>
    /// Gets all registered function registrations
    /// </summary>
    IEnumerable<FunctionRegistration> GetRegistrations();

    /// <summary>
    /// Gets a function registration by ID
    /// </summary>
    FunctionRegistration? GetRegistration(string functionId);
}

/// <summary>
/// Implementation of the function registry
/// </summary>
internal sealed class InngestFunctionRegistry : IInngestFunctionRegistry
{
    private readonly Dictionary<string, FunctionRegistration> _registrations = new();
    private readonly string _appId;

    public InngestFunctionRegistry(string appId)
    {
        _appId = appId;
    }

    public void RegisterFunction(Type functionType)
    {
        var registration = CreateRegistration(functionType);
        if (registration != null)
        {
            var fullId = $"{_appId}-{registration.Id}";
            _registrations[fullId] = registration;
        }
    }

    public void RegisterFunctionsFromAssembly(Assembly assembly)
    {
        var functionTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<InngestFunctionAttribute>() != null)
            .Where(t => typeof(IInngestFunction).IsAssignableFrom(t) ||
                        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInngestFunction<>)));

        foreach (var type in functionTypes)
        {
            RegisterFunction(type);
        }
    }

    public IEnumerable<FunctionRegistration> GetRegistrations() => _registrations.Values;

    public FunctionRegistration? GetRegistration(string functionId)
    {
        _registrations.TryGetValue(functionId, out var registration);
        return registration;
    }

    private FunctionRegistration? CreateRegistration(Type functionType)
    {
        var functionAttr = functionType.GetCustomAttribute<InngestFunctionAttribute>();
        if (functionAttr == null)
            return null;

        // Determine event data type for typed handlers
        Type? eventDataType = null;
        var typedInterface = functionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInngestFunction<>));
        if (typedInterface != null)
        {
            eventDataType = typedInterface.GetGenericArguments()[0];
        }

        // Get triggers - pass event data type for auto-derivation
        var triggers = GetTriggers(functionType, eventDataType);
        if (triggers.Length == 0)
        {
            // Default to event trigger with function ID as event name
            triggers = new[] { FunctionTrigger.CreateEventTrigger(functionAttr.Id) };
        }

        // Get options from attributes
        var options = GetFunctionOptions(functionType);

        return new FunctionRegistration
        {
            Id = functionAttr.Id,
            Name = functionAttr.Name ?? functionAttr.Id,
            FunctionType = functionType,
            Triggers = triggers,
            Options = options,
            EventDataType = eventDataType
        };
    }

    private static FunctionTrigger[] GetTriggers(Type functionType, Type? eventDataType)
    {
        var triggers = new List<FunctionTrigger>();

        // Event triggers from attributes
        var eventTriggers = functionType.GetCustomAttributes<EventTriggerAttribute>();
        foreach (var et in eventTriggers)
        {
            var trigger = FunctionTrigger.CreateEventTrigger(et.Event);
            if (!string.IsNullOrEmpty(et.Expression))
            {
                trigger.Constraint = new EventConstraint { Expression = et.Expression };
            }
            triggers.Add(trigger);
        }

        // Cron triggers
        var cronTriggers = functionType.GetCustomAttributes<CronTriggerAttribute>();
        foreach (var ct in cronTriggers)
        {
            triggers.Add(FunctionTrigger.CreateCronTrigger(ct.Cron));
        }

        // Auto-derive event trigger from IInngestFunction<T> when T implements IInngestEventData
        // or has [InngestEvent] attribute, but only if no explicit [EventTrigger] was specified
        if (triggers.Count == 0 && eventDataType != null)
        {
            var derivedEventName = TryGetEventNameFromType(eventDataType);
            if (derivedEventName != null)
            {
                triggers.Add(FunctionTrigger.CreateEventTrigger(derivedEventName));
            }
        }

        return triggers.ToArray();
    }

    /// <summary>
    /// Attempts to get the event name from a type that implements IInngestEventData
    /// or is decorated with [InngestEvent].
    /// </summary>
    private static string? TryGetEventNameFromType(Type eventDataType)
    {
        // Check for IInngestEventData implementation (static abstract EventName property)
        if (typeof(IInngestEventData).IsAssignableFrom(eventDataType))
        {
            var property = eventDataType.GetProperty("EventName", BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                var value = property.GetValue(null) as string;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        // Check for [InngestEvent] attribute
        var eventAttr = eventDataType.GetCustomAttribute<InngestEventAttribute>();
        if (eventAttr != null)
        {
            return eventAttr.Name;
        }

        return null;
    }

    private static FunctionOptions? GetFunctionOptions(Type functionType)
    {
        var options = new FunctionOptions();
        bool hasOptions = false;

        // Retry
        var retryAttr = functionType.GetCustomAttribute<RetryAttribute>();
        if (retryAttr != null)
        {
            options.Retry = new RetryOptions { Attempts = retryAttr.Attempts };
            hasOptions = true;
        }

        // Concurrency
        var concurrencyAttr = functionType.GetCustomAttribute<ConcurrencyAttribute>();
        if (concurrencyAttr != null)
        {
            options.ConcurrencyOptions = new ConcurrencyOptions
            {
                Limit = concurrencyAttr.Limit,
                Key = concurrencyAttr.Key,
                Scope = concurrencyAttr.Scope
            };
            hasOptions = true;
        }

        // Rate limit
        var rateLimitAttr = functionType.GetCustomAttribute<RateLimitAttribute>();
        if (rateLimitAttr != null)
        {
            options.RateLimit = new RateLimitOptions
            {
                Limit = rateLimitAttr.Limit,
                Period = rateLimitAttr.Period,
                Key = rateLimitAttr.Key
            };
            hasOptions = true;
        }

        return hasOptions ? options : null;
    }
}
