using System.Collections.Generic;

namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     The outcome of processing a single file: the users assigned to it and any file-level or
///     user-level errors encountered while processing it.
/// </summary>
public class FileUserResult(string displayName, int? fileId, string? uncPath, string share)
{
    // A human-readable label identifying the file in the spreadsheet (its UNC path when known,
    // otherwise its identifier).
    public string DisplayName { get; } = displayName;

    public int? FileId { get; } = fileId;

    public string? UncPath { get; } = uncPath;

    // The "share" name extracted from the UNC path (per Output:ShareSegmentIndex). Emitted as a
    // dedicated column in the spreadsheet so Excel's autofilter can group rows by share without
    // needing separate worksheets. Empty when no UNC path is known (Phase 0 lookup failures) or
    // when the path is shallower than the configured segment index.
    public string Share { get; } = share;

    public List<ReportedUser> Users { get; } = [];

    public List<string> Errors { get; } = [];

    public void AddError(string error) => this.Errors.Add(error);
}
