using System.Collections.Generic;

namespace LegacyRecordsCaseWareFileUsers.Models;

/// <summary>
///     The outcome of processing a single file: the users assigned to it and any file-level or
///     user-level errors encountered while processing it.
/// </summary>
public class FileUserResult
{
    public FileUserResult(string displayName, int? fileId, string? uncPath)
    {
        this.DisplayName = displayName;
        this.FileId = fileId;
        this.UncPath = uncPath;
    }

    // A human-readable label identifying the file in the spreadsheet (its UNC path when known,
    // otherwise its identifier).
    public string DisplayName { get; }

    public int? FileId { get; }

    public string? UncPath { get; }

    public List<ReportedUser> Users { get; } = new();

    public List<string> Errors { get; } = new();

    public void AddError(string error)
    {
        this.Errors.Add(error);
    }
}
