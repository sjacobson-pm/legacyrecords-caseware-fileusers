namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Carries the current run's identifier and derived artifact paths. Registered as a singleton
///     for the lifetime of the process — one <see cref="IRunContext" /> per invocation of the tool.
/// </summary>
public interface IRunContext
{
    /// <summary>
    ///     Gets the memorable run identifier of the form <c>{adjective}-{animal}-{verb}</c>
    ///     (for example, <c>swift-otter-runs</c>). Composed once at startup and never changed.
    /// </summary>
    /// <value>The run identifier for this invocation of the tool.</value>
    string RunId { get; }

    /// <summary>
    ///     Gets the directory where this run's artifacts (spreadsheet, log, journal) are written.
    ///     Resolved once at startup from CLI <c>--output</c>, then <c>Output:FilePath</c>, then
    ///     <c>Output:Directory</c>, then the current working directory — the same precedence used
    ///     for the spreadsheet output path. Every artifact for this run lives here.
    /// </summary>
    /// <value>An absolute-or-relative directory path; never <c>null</c> or empty.</value>
    string EffectiveOutputDirectory { get; }
}
