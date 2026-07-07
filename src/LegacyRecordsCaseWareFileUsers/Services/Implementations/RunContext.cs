using System;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Immutable implementation of <see cref="IRunContext" />. The run identifier is chosen once by
///     <see cref="Helpers.RunIdGenerator" /> before the DI container is built and is then registered
///     as a singleton so every collaborator observes the same value.
/// </summary>
internal sealed class RunContext : IRunContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RunContext" /> class.
    /// </summary>
    /// <param name="runId">The run identifier chosen at startup.</param>
    /// <param name="effectiveOutputDirectory">
    ///     The directory where the spreadsheet, log, and journal for this run will be written.
    /// </param>
    public RunContext(string runId, string effectiveOutputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveOutputDirectory);

        this.RunId = runId;
        this.EffectiveOutputDirectory = effectiveOutputDirectory;
    }

    /// <inheritdoc />
    public string RunId { get; }

    /// <inheritdoc />
    public string EffectiveOutputDirectory { get; }
}
