using Inngest.Configuration;

namespace Inngest.Tests;

/// <summary>
/// Tests for InngestOptions and configuration
/// </summary>
public class ConfigurationTests
{
    [Fact]
    public void InngestOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new InngestOptions();

        // Assert
        Assert.Null(options.EventKey);
        Assert.Null(options.SigningKey);
        Assert.Null(options.AppId);
        Assert.Null(options.IsDev);
        Assert.False(options.DisableCronTriggersInDev);
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_SetsDevServerUrl()
    {
        // Arrange
        var options = new InngestOptions();

        // Act
        options.ApplyEnvironmentFallbacks();

        // Assert
        Assert.Equal("http://localhost:8288", options.DevServerUrl);
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsInngestDevEnvVar()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DEV", "true");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.True(options.IsDev);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_InngestDevUrl_SetsDevServerUrl()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DEV", "http://custom-dev:8000");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.True(options.IsDev);
            Assert.Equal("http://custom-dev:8000", options.DevServerUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_InngestDevFalse_SetsDevToFalse()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DEV", "false");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.False(options.IsDev);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DEV", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsEventKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_EVENT_KEY", "test-event-key-123");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("test-event-key-123", options.EventKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_EVENT_KEY", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsSigningKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_SIGNING_KEY", "signkey-test-abc123");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("signkey-test-abc123", options.SigningKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_SIGNING_KEY", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsDisableCronInDev()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DISABLE_CRON_IN_DEV", "true");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.True(options.DisableCronTriggersInDev);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DISABLE_CRON_IN_DEV", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_DisableCronInDev_AcceptsOne()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_DISABLE_CRON_IN_DEV", "1");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.True(options.DisableCronTriggersInDev);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_DISABLE_CRON_IN_DEV", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ExplicitValueTakesPrecedence()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_EVENT_KEY", "env-key");
        try
        {
            var options = new InngestOptions
            {
                EventKey = "explicit-key"
            };

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("explicit-key", options.EventKey); // Explicit value preserved
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_EVENT_KEY", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsAppId()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_APP_ID", "my-app-from-env");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("my-app-from-env", options.AppId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_APP_ID", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsServeOrigin()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_SERVE_ORIGIN", "https://my-app.com");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("https://my-app.com", options.ServeOrigin);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_SERVE_ORIGIN", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsServePath()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_SERVE_PATH", "/custom/inngest");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("/custom/inngest", options.ServePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_SERVE_PATH", null);
        }
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_DefaultsEnvironmentToDev()
    {
        // Arrange
        var options = new InngestOptions();

        // Act
        options.ApplyEnvironmentFallbacks();

        // Assert
        Assert.Equal("dev", options.Environment);
    }

    [Fact]
    public void ApplyEnvironmentFallbacks_ReadsEnvironment()
    {
        // Arrange
        Environment.SetEnvironmentVariable("INNGEST_ENV", "production");
        try
        {
            var options = new InngestOptions();

            // Act
            options.ApplyEnvironmentFallbacks();

            // Assert
            Assert.Equal("production", options.Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INNGEST_ENV", null);
        }
    }
}
