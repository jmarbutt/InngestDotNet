using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Inngest;

public static class InngestMiddlewareExtensions
{
    public static IApplicationBuilder UseInngest(this IApplicationBuilder app, string path = "/api/inngest")
    {
        return app.Map(path, builder =>
        {
            builder.Run(async context =>
            {
                var inngestClient = context.RequestServices.GetRequiredService<InngestClient>();
                await inngestClient.HandleRequestAsync(context);
            });
        });
    }

    public static IServiceCollection AddInngest(this IServiceCollection services, string eventKey, string signingKey)
    {
        return services.AddSingleton(new InngestClient(eventKey, signingKey));
    }
}