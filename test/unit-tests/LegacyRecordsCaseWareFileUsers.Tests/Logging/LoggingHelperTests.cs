using AutoFixture.Xunit3;
using LegacyRecordsCaseWareFileUsers.Logging;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Logging;

public class LoggingHelperTests
{
    [Theory]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    [InlineData("fatal", LogEventLevel.Fatal)]
    public void GetLogEventLevel_ValidLogLevel_ReturnsLogLevel(string logLevel, LogEventLevel expected)
    {
        // Arrange

        // Act
        var actual = LoggingHelper.GetLogEventLevel(logLevel);

        // Assert
        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineAutoData(LogEventLevel.Debug)]
    public void GetLogEventLevel_InvalidLogLevel_ReturnsDebugLogLevel(LogEventLevel expected, string logLevel)
    {
        // Arrange

        // Act
        var actual = LoggingHelper.GetLogEventLevel(logLevel);

        // Assert
        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    [InlineData("fatal", LogEventLevel.Fatal)]
    public void GetLoggingLevelSwitch_ValidLogLevel_ReturnsSwitchWithProperLogLevel(string logLevel, LogEventLevel expectedLogEventLevel)
    {
        // Arrange
        var expected = new LoggingLevelSwitch { MinimumLevel = expectedLogEventLevel };

        // Act
        var actual = LoggingHelper.GetLoggingLevelSwitch(logLevel);

        // Assert
        actual.ShouldBeEquivalentTo(expected);
    }

    [Theory]
    [InlineAutoData(LogEventLevel.Debug)]
    public void GetLoggingLevelSwitch_InvalidLogLevel_ReturnsSwitchWithDebugLogLevel(LogEventLevel expectedLogEventLevel, string logLevel)
    {
        // Arrange
        var expected = new LoggingLevelSwitch { MinimumLevel = expectedLogEventLevel };

        // Act
        var actual = LoggingHelper.GetLoggingLevelSwitch(logLevel);

        // Assert
        actual.ShouldBeEquivalentTo(expected);
    }
}
