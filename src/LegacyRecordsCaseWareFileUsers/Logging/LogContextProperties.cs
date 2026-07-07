namespace LegacyRecordsCaseWareFileUsers.Logging;

public static class LogContextProperties
{
    public const string EngineInstance = "EngineInstanceId";

    public const string ExecutionMode = "ExecutionMode";

    public const string MachineName = "MachineName";

    // Rendered by the console/debug/file output templates at the start of every message. When set
    // (via PhaseScope), it applies the phase indent to every log line emitted inside the phase;
    // when unset, it renders as an empty string.
    public const string PhaseIndent = "PhaseIndent";

    public const string RunId = "RunId";
}
