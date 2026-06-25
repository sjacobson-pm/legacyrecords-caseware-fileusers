using System;
using Serilog.Core;
using Serilog.Events;

namespace LegacyRecordsCaseWareFileUsers.Logging;

public static class LoggingHelper
{
    public static LogEventLevel GetLogEventLevel(string logLevel)
    {
        return Enum.TryParse(logLevel, true, out LogEventLevel logEventLevel) ? logEventLevel : LogEventLevel.Debug;
    }

    public static LoggingLevelSwitch GetLoggingLevelSwitch(string logLevel)
    {
        var loggingLevelSwitch = new LoggingLevelSwitch { MinimumLevel = GetLogEventLevel(logLevel) };

        return loggingLevelSwitch;
    }
}
