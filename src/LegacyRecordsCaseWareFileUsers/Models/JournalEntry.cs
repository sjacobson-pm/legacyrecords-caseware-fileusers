using System;
using System.Collections.Generic;

namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     One durable record of a file's Phase 1 outcome. Emitted by <c>RunJournal</c> as a single JSON
///     line at the end of <c>ProcessStagedFile</c> — after the CaseWare session closes and
///     <see cref="UserIdentifiers" /> or <see cref="Errors" /> is known, but before the workspace is
///     deleted — so a crash before file N+1 never loses file N's result.
///     <para>
///         On resume, entries are read back from the journal and matched to input work items by
///         <see cref="NormalizedKey" />. When multiple entries share a key (e.g., a file was retried
///         via <c>Processing:RetryErroredFilesOnResume</c>), the last entry wins — that matches the
///         natural "trust the journal" model where the most recent recorded outcome is the truth.
///     </para>
/// </summary>
public sealed record JournalEntry
{
    /// <summary>Gets the UTC timestamp when the entry was recorded, in ISO 8601 form.</summary>
    /// <value>The UTC timestamp.</value>
    public required DateTime Timestamp { get; init; }

    /// <summary>Gets the original UNC path exactly as it was consumed from the input.</summary>
    /// <value>The UNC path.</value>
    public required string UncPath { get; init; }

    /// <summary>
    ///     Gets the normalized identity key used for resume matching. Computed as
    ///     <c>Path.GetFullPath(UncPath).ToLowerInvariant()</c> so case-insensitive UNC paths compare
    ///     equal regardless of the input's casing.
    /// </summary>
    /// <value>The normalized identity key.</value>
    public required string NormalizedKey { get; init; }

    /// <summary>
    ///     Gets the numeric file ID from the input, if the input was an integer rather than a UNC
    ///     path. Preserved for debuggability; not used for resume matching.
    /// </summary>
    /// <value>The file ID, or <c>null</c> if the input was a UNC path.</value>
    public int? FileId { get; init; }

    /// <summary>Gets the human-readable label emitted into the spreadsheet.</summary>
    /// <value>The display label.</value>
    public required string DisplayName { get; init; }

    /// <summary>Gets the FILE-group user identifiers CaseWare returned for the file.</summary>
    /// <value>The user identifiers.</value>
    public required IReadOnlyList<string> UserIdentifiers { get; init; }

    /// <summary>Gets any file-level errors accumulated during Phase 0 or Phase 1.</summary>
    /// <value>The file-level errors.</value>
    public required IReadOnlyList<string> Errors { get; init; }
}
