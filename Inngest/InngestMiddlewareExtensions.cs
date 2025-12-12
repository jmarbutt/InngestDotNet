using Inngest.Configuration;
using Inngest.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inngest;

/// <summary>
/// Extension methods for integrating Inngest with ASP.NET Core
/// </summary>
public static class InngestMiddlewareExtensions
{
    /// <summary>
    /// Adds Inngest middleware to the application pipeline
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="path">The path where Inngest functions will be registered (default: /api/inngest)</param>
    /// <returns>The application builder for further configuration</returns>
    public static IApplicationBuilder UseInngest(this IApplicationBuilder app, string path = "/api/inngest")
    {
        return app.Map(path, builder =>
        {
            builder.Run(async context =>
            {
                var inngestClient = context.RequestServices.GetRequiredService<IInngestClient>();
                await inngestClient.HandleRequestAsync(context);
            });
        });
    }

    /// <summary>
    /// Registers Inngest services with the dependency injection container using the options pattern.
    /// This is the recommended way to configure Inngest for production applications.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure Inngest options</param>
    /// <returns>An InngestBuilder for registering functions</returns>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddInngest(options =>
    ///     {
    ///         options.AppId = "my-app";
    ///         options.EventKey = "...";
    ///         options.SigningKey = "...";
    ///     })
    ///     .AddFunction&lt;OrderProcessor&gt;()
    ///     .AddFunctionsFromAssembly(typeof(Program).Assembly);
    /// </code>
    /// </example>
    public static InngestBuilder AddInngest(this IServiceCollection services, Action<InngestOptions>? configure = null)
    {
        // Configure options
        var optionsBuilder = services.AddOptions<InngestOptions>();
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.PostConfigure(options =>
        {
            options.ApplyEnvironmentFallbacks();
            options.Validate();
        });

        // Create a shared builder that will be populated with function types
        var inngestBuilder = new InngestBuilder(services);

        // Register the function registry (will be populated when resolved)
        services.AddSingleton<IInngestFunctionRegistry>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<InngestOptions>>().Value;
            var appId = options.AppId ?? "inngest-app";
            var registry = new InngestFunctionRegistry(appId);

            // Register all function types that were added via the builder
            foreach (var functionType in inngestBuilder.GetFunctionTypes())
            {
                registry.RegisterFunction(functionType);
            }

            return registry;
        });

        // Register the Inngest client
        services.AddSingleton<IInngestClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<InngestOptions>>().Value;
            var registry = sp.GetRequiredService<IInngestFunctionRegistry>();
            var logger = sp.GetService<ILogger<InngestClient>>();

            return new InngestClient(options, registry, sp, logger: logger);
        });

        return inngestBuilder;
    }

    /// <summary>
    /// Registers Inngest services with configuration from an IConfigurationSection.
    /// Typically used to bind from appsettings.json.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration section containing Inngest settings</param>
    /// <returns>An InngestBuilder for registering functions</returns>
    /// <example>
    /// <code>
    /// // appsettings.json:
    /// // {
    /// //   "Inngest": {
    /// //     "AppId": "my-app",
    /// //     "EventKey": "...",
    /// //     "SigningKey": "..."
    /// //   }
    /// // }
    ///
    /// builder.Services
    ///     .AddInngest(builder.Configuration.GetSection("Inngest"))
    ///     .AddFunctionsFromAssembly(typeof(Program).Assembly);
    /// </code>
    /// </example>
    public static InngestBuilder AddInngest(this IServiceCollection services, IConfigurationSection configuration)
    {
        // Bind configuration to options
        services.Configure<InngestOptions>(configuration);
        services.PostConfigure<InngestOptions>(options =>
        {
            options.ApplyEnvironmentFallbacks();
            options.Validate();
        });

        // Create a shared builder that will be populated with function types
        var inngestBuilder = new InngestBuilder(services);

        // Register the function registry (will be populated when resolved)
        services.AddSingleton<IInngestFunctionRegistry>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<InngestOptions>>().Value;
            var appId = options.AppId ?? "inngest-app";
            var registry = new InngestFunctionRegistry(appId);

            // Register all function types that were added via the builder
            foreach (var functionType in inngestBuilder.GetFunctionTypes())
            {
                registry.RegisterFunction(functionType);
            }

            return registry;
        });

        // Register the Inngest client
        services.AddSingleton<IInngestClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<InngestOptions>>().Value;
            var registry = sp.GetRequiredService<IInngestFunctionRegistry>();
            var logger = sp.GetService<ILogger<InngestClient>>();

            return new InngestClient(options, registry, sp, logger: logger);
        });

        return inngestBuilder;
    }

}
