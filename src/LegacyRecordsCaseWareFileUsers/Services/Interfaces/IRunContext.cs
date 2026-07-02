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
}
