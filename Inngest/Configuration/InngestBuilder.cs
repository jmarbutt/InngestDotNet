using System.Reflection;
using Inngest.Attributes;
using Inngest.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Inngest.Configuration;

/// <summary>
/// Builder for configuring Inngest services and registering functions
/// </summary>
public class InngestBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Type> _functionTypes = new();
    private readonly List<Assembly> _assembliesToScan = new();

    internal InngestBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers an Inngest function type.
    /// The function must be decorated with [InngestFunction] and implement IInngestFunction.
    /// </summary>
    /// <typeparam name="TFunction">The function type to register</typeparam>
    /// <returns>The builder for chaining</returns>
    public InngestBuilder AddFunction<TFunction>() where TFunction : class
    {
        return AddFunction(typeof(TFunction));
    }

    /// <summary>
    /// Registers an Inngest function type.
    /// </summary>
    /// <param name="functionType">The function type to register</param>
    /// <returns>The builder for chaining</returns>
    public InngestBuilder AddFunction(Type functionType)
    {
        ValidateFunctionType(functionType);
        _functionTypes.Add(functionType);

        // Register the function type with DI for constructor injection
        _services.AddScoped(functionType);

        return this;
    }

    /// <summary>
    /// Scans an assembly for Inngest functions and registers all found functions.
    /// Functions must be decorated with [InngestFunction] and implement IInngestFunction.
    /// </summary>
    /// <param name="assembly">The assembly to scan</param>
    /// <returns>The builder for chaining</returns>
    public InngestBuilder AddFunctionsFromAssembly(Assembly assembly)
    {
        _assembliesToScan.Add(assembly);

        // Find and register all function types
        var functionTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<InngestFunctionAttribute>() != null)
            .Where(IsFunctionType);

        foreach (var type in functionTypes)
        {
            if (!_functionTypes.Contains(type))
            {
                _functionTypes.Add(type);
                _services.AddScoped(type);
            }
        }

        return this;
    }

    /// <summary>
    /// Scans the assembly containing the specified type for Inngest functions.
    /// </summary>
    /// <typeparam name="T">A type in the assembly to scan</typeparam>
    /// <returns>The builder for chaining</returns>
    public InngestBuilder AddFunctionsFromAssemblyContaining<T>()
    {
        return AddFunctionsFromAssembly(typeof(T).Assembly);
    }

    internal IReadOnlyList<Type> GetFunctionTypes() => _functionTypes;
    internal IReadOnlyList<Assembly> GetAssembliesToScan() => _assembliesToScan;

    private static void ValidateFunctionType(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            throw new ArgumentException(
                $"Type {type.Name} must be a non-abstract class", nameof(type));
        }

        if (type.GetCustomAttribute<InngestFunctionAttribute>() == null)
        {
            throw new ArgumentException(
                $"Type {type.Name} must be decorated with [InngestFunction]", nameof(type));
        }

        if (!IsFunctionType(type))
        {
            throw new ArgumentException(
                $"Type {type.Name} must implement IInngestFunction or IInngestFunction<TEventData>", nameof(type));
        }
    }

    private static bool IsFunctionType(Type type)
    {
        return typeof(IInngestFunction).IsAssignableFrom(type) ||
               type.GetInterfaces().Any(i =>
                   i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInngestFunction<>));
    }
}
