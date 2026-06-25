using System.Collections.Generic;
using LegacyRecordsCaseWareFileUsers.Models;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Writes per-file user results to a spreadsheet.
/// </summary>
public interface IFileUserSpreadsheetWriter
{
    /// <summary>
    ///     Writes the per-file user results to a spreadsheet, grouped by file, including any
    ///     file-level or user-level errors.
    /// </summary>
    /// <param name="results">The per-file results.</param>
    /// <param name="outputFilePath">The full path of the .xlsx file to write.</param>
    void Write(IReadOnlyList<FileUserResult> results, string outputFilePath);
}
