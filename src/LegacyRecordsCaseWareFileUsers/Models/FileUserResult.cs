using System.Collections.Generic;

namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     The outcome of processing a single file: the users assigned to it and any file-level or
///     user-level errors encountered while processing it.
/// </summary>
public class FileUserResult(string displayName, int? fileId, string? uncPath)
{
    // A human-readable label identifying the file in the spreadsheet (its UNC path when known,
    // otherwise its identifier).
    public string DisplayName { get; } = displayName;

    public int? FileId { get; } = fileId;

    public string? UncPath { get; } = uncPath;

    public List<ReportedUser> Users { get; } = [];

    public List<string> Errors { get; } = [];

    public void AddError(string error) => this.Errors.Add(error);
}
