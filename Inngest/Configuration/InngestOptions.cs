using System.ComponentModel.DataAnnotations;

namespace Inngest.Configuration;

/// <summary>
/// Configuration options for the Inngest SDK
/// </summary>
public class InngestOptions
{
    /// <summary>
    /// Your Inngest event key for sending events
    /// Falls back to INNGEST_EVENT_KEY environment variable
    /// </summary>
    public string? EventKey { get; set; }

    /// <summary>
    /// Your Inngest signing key for request verification
    /// Falls back to INNGEST_SIGNING_KEY environment variable
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Fallback signing key for key rotation scenarios
    /// Falls back to INNGEST_SIGNING_KEY_FALLBACK environment variable
    /// </summary>
    public string? SigningKeyFallback { get; set; }

    /// <summary>
    /// Application identifier used in function IDs
    /// Falls back to INNGEST_APP_ID environment variable
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Base URL for the Inngest API
    /// Falls back to INNGEST_API_BASE_URL environment variable
    /// Defaults to https://api.inngest.com or dev server URL
    /// </summary>
    public string? ApiOrigin { get; set; }

    /// <summary>
    /// Base URL for the Inngest Event API
    /// Falls back to INNGEST_EVENT_API_BASE_URL environment variable
    /// Defaults to https://inn.gs or dev server URL
    /// </summary>
    public string? EventApiOrigin { get; set; }

    /// <summary>
    /// Environment name (e.g., "production", "staging")
    /// Falls back to INNGEST_ENV environment variable
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Enable dev mode for local development
    /// Falls back to INNGEST_DEV environment variable
    /// Set to false explicitly to force production mode even locally
    /// </summary>
    public bool? IsDev { get; set; }

    /// <summary>
    /// Dev server URL when IsDev is true
    /// Falls back to INNGEST_DEV environment variable value if it's a URL
    /// Defaults to http://localhost:8288
    /// </summary>
    public string? DevServerUrl { get; set; }

    /// <summary>
    /// Base URL for Inngest to reach this service
    /// Falls back to INNGEST_SERVE_ORIGIN environment variable
    /// </summary>
    public string? ServeOrigin { get; set; }

    /// <summary>
    /// Path for the Inngest endpoint
    /// Falls back to INNGEST_SERVE_PATH environment variable
    /// </summary>
    public string? ServePath { get; set; }

    /// <summary>
    /// When true, cron triggers are excluded from function registration in dev mode.
    /// This prevents scheduled functions from running during local development.
    /// Falls back to INNGEST_DISABLE_CRON_IN_DEV environment variable.
    /// Defaults to false for backward compatibility.
    /// </summary>
    public bool DisableCronTriggersInDev { get; set; } = false;

    /// <summary>
    /// Validates the configuration and throws if invalid
    /// </summary>
    internal void Validate()
    {
        // AppId is required when not in dev mode
        if (string.IsNullOrEmpty(AppId) && IsDev != true)
        {
            var envAppId = System.Environment.GetEnvironmentVariable("INNGEST_APP_ID");
            if (string.IsNullOrEmpty(envAppId))
            {
                // AppId will default to assembly name, so this is just a warning scenario
            }
        }
    }

    /// <summary>
    /// Applies environment variable fallbacks to any unset properties
    /// </summary>
    internal void ApplyEnvironmentFallbacks()
    {
        EventKey ??= System.Environment.GetEnvironmentVariable("INNGEST_EVENT_KEY");
        SigningKey ??= System.Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY");
        SigningKeyFallback ??= System.Environment.GetEnvironmentVariable("INNGEST_SIGNING_KEY_FALLBACK");
        AppId ??= System.Environment.GetEnvironmentVariable("INNGEST_APP_ID");
        ApiOrigin ??= System.Environment.GetEnvironmentVariable("INNGEST_API_BASE_URL");
        EventApiOrigin ??= System.Environment.GetEnvironmentVariable("INNGEST_EVENT_API_BASE_URL");
        Environment ??= System.Environment.GetEnvironmentVariable("INNGEST_ENV") ?? "dev";
        ServeOrigin ??= System.Environment.GetEnvironmentVariable("INNGEST_SERVE_ORIGIN");
        ServePath ??= System.Environment.GetEnvironmentVariable("INNGEST_SERVE_PATH");

        // Check for dev mode - only apply env var if IsDev wasn't explicitly set
        if (!IsDev.HasValue)
        {
            var devEnv = System.Environment.GetEnvironmentVariable("INNGEST_DEV");
            if (!string.IsNullOrEmpty(devEnv))
            {
                // Check for explicit false/0 values
                if (devEnv.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    devEnv.Equals("0", StringComparison.OrdinalIgnoreCase))
                {
                    IsDev = false;
                }
                else
                {
                    IsDev = true;
                    // If INNGEST_DEV contains a URL, use it as the dev server URL
                    if (devEnv.StartsWith("http://") || devEnv.StartsWith("https://"))
                    {
                        DevServerUrl = devEnv;
                    }
                }
            }
        }

        // Default to false (production mode) if not set
        IsDev ??= false;

        DevServerUrl ??= "http://localhost:8288";

        // Check for disable cron in dev mode - only apply env var if not explicitly set
        var disableCronEnv = System.Environment.GetEnvironmentVariable("INNGEST_DISABLE_CRON_IN_DEV");
        if (!string.IsNullOrEmpty(disableCronEnv) &&
            (disableCronEnv.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             disableCronEnv.Equals("1", StringComparison.OrdinalIgnoreCase)))
        {
            DisableCronTriggersInDev = true;
        }
    }
}
