using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
    /// Registers Inngest services with the dependency injection container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="eventKey">The Inngest event key for your application</param>
    /// <param name="signingKey">The Inngest signing key for your application</param>
    /// <param name="apiOrigin">Optional custom API origin URL</param>
    /// <param name="eventApiOrigin">Optional custom event API origin URL</param>
    /// <returns>The service collection for further configuration</returns>
    public static IServiceCollection AddInngest(this IServiceCollection services, string eventKey, string signingKey, string? apiOrigin = null, string? eventApiOrigin = null)
    {
        return services.AddSingleton<IInngestClient>(sp => 
        {
            if (apiOrigin != null && eventApiOrigin != null)
            {
                return new InngestClient(eventKey, signingKey, apiOrigin, eventApiOrigin);
            }
            return new InngestClient(eventKey, signingKey);
        });
    }
}