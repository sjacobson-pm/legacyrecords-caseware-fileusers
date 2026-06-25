using System.Collections.Generic;
using LegacyRecordsCaseWareFileUsers.Options;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Options;

public class ConfigurationOptionsTests
{
    [Fact]
    public void Bind_ApplicationLoggingSection_PopulatesLoggingOptions()
    {
        // Arrange — the logging settings live under the custom "ApplicationLogging" key,
        // mapped to ConfigurationOptions.Logging via [ConfigurationKeyName].
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationLogging:ConsoleOutputTemplate"] = "console-template",
                ["ApplicationLogging:LogLevel:Default"] = "Warning",
                ["ApplicationLogging:ApplicationInsights:ConnectionString"] = "ai-connection",
            })
            .Build();

        var options = new ConfigurationOptions();

        // Act
        configuration.Bind(options);

        // Assert
        options.Logging.ShouldNotBeNull();
        options.Logging.ConsoleOutputTemplate.ShouldBe("console-template");
        options.Logging.LogLevel.Default.ShouldBe("Warning");
        options.Logging.ApplicationInsights.ConnectionString.ShouldBe("ai-connection");
    }
}
