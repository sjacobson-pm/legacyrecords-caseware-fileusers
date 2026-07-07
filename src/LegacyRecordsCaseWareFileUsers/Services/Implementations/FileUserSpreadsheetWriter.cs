using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Writes per-file user results to an .xlsx spreadsheet using ClosedXML. Output is a single
///     "File Users" worksheet formatted as an Excel Table (autofilter, frozen header row, banded
///     rows), one row per (file × user) pair. Files with errors produce an additional row per
///     error; files with neither users nor errors produce a single informational placeholder row.
///     Input order is preserved across files, and users within a file are sorted alphabetically by
///     name (same as the prior block-per-file output).
///     <para>
///         The Share column is populated from <c>OutputOptions.ShareSegmentIndex</c> so consumers
///         can filter or group by share directly in Excel without needing separate worksheets.
///     </para>
/// </summary>
internal class FileUserSpreadsheetWriter : IFileUserSpreadsheetWriter
{
    private const string WorksheetName = "File Users";
    private const string TableName = "FileUsers";
    private const string NoUsersInformationalNote = "(no users assigned in the FILE security group)";

    private readonly ILogger<FileUserSpreadsheetWriter> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FileUserSpreadsheetWriter" /> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public FileUserSpreadsheetWriter(ILogger<FileUserSpreadsheetWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    /// <inheritdoc />
    public void Write(IReadOnlyList<FileUserResult> results, string outputFilePath)
    {
        ArgumentNullException.ThrowIfNull(results);

        this.logger.LogInformation("Writing spreadsheet to {OutputFilePath}...", outputFilePath);

        var directory = Path.GetDirectoryName(outputFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(WorksheetName);

        // header row — must be present even for empty result sets so the Excel Table has a valid
        // shape; the AutoFilter will then be a no-op instead of throwing on an empty range
        WriteHeaderRow(worksheet);

        var lastDataRow = WriteAllDataRows(worksheet, results);

        // Excel Table on the populated range: gives users free autofilter dropdowns on every
        // column, frozen header row, and banded row shading. If no data rows were emitted, size
        // the table to just the header row.
        var lastRowForTable = lastDataRow >= 2 ? lastDataRow : 2;
        if (lastDataRow < 2)
        {
            // ensure at least one row exists under the header so ClosedXML can form a table range
            worksheet.Cell(2, 1).Value = string.Empty;
        }

        var table = worksheet.Range(1, 1, lastRowForTable, 6).CreateTable(TableName);
        table.ShowAutoFilter = true;
        table.Theme = XLTableTheme.TableStyleLight9;

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns(1, 6).AdjustToContents();

        workbook.SaveAs(outputFilePath);

        this.logger.LogInformation(
            "Finished writing spreadsheet for {FileCount} file(s) → {DataRowCount} data row(s).",
            results.Count,
            Math.Max(0, lastDataRow - 1));
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet)
    {
        worksheet.Cell(1, 1).Value = "Share";
        worksheet.Cell(1, 2).Value = "File";
        worksheet.Cell(1, 3).Value = "Full Name";
        worksheet.Cell(1, 4).Value = "Office";
        worksheet.Cell(1, 5).Value = "Position";
        worksheet.Cell(1, 6).Value = "Errors";
    }

    /// <summary>
    ///     Writes every data row for every file result in input order, returning the last row
    ///     number used (1 if no data rows were emitted — only the header is present).
    /// </summary>
    /// <param name="worksheet">The worksheet to write into.</param>
    /// <param name="results">The per-file results in input order.</param>
    /// <returns>The row number of the last populated row (1 when only the header exists).</returns>
    private static int WriteAllDataRows(IXLWorksheet worksheet, IReadOnlyList<FileUserResult> results)
    {
        var row = 2; // row 1 is the header

        foreach (var result in results)
        {
            // A file with users emits one row per user. A file with errors emits an additional row
            // per error (user columns blank, Errors populated). A file with neither users nor
            // errors emits a single placeholder row so the file still appears in the sheet — its
            // Errors column carries an informational note explaining why it looks empty.
            var wroteAnyRow = false;

            var fileLabel = FormatFileForColumn(result);

            foreach (var user in result.Users)
            {
                worksheet.Cell(row, 1).Value = result.Share;
                worksheet.Cell(row, 2).Value = fileLabel;
                worksheet.Cell(row, 3).Value = user.FullName;
                worksheet.Cell(row, 4).Value = user.Office;
                worksheet.Cell(row, 5).Value = user.Position;

                // Errors column is left blank for user rows so filtering by non-empty Errors
                // finds only the error/placeholder rows.
                row++;
                wroteAnyRow = true;
            }

            foreach (var error in result.Errors)
            {
                worksheet.Cell(row, 1).Value = result.Share;
                worksheet.Cell(row, 2).Value = fileLabel;

                // user columns intentionally blank on error rows
                worksheet.Cell(row, 6).Value = error;
                row++;
                wroteAnyRow = true;
            }

            if (!wroteAnyRow)
            {
                worksheet.Cell(row, 1).Value = result.Share;
                worksheet.Cell(row, 2).Value = fileLabel;
                worksheet.Cell(row, 6).Value = NoUsersInformationalNote;
                row++;
            }
        }

        return row - 1;
    }

    /// <summary>
    ///     Formats the <c>File</c> column value for a result. When a UNC path is known and the
    ///     Share column captures a prefix of it, returns the portion of the path <em>after</em>
    ///     the share prefix — so <c>Share + "\" + File</c> reconstructs the full UNC path
    ///     regardless of intermediate depth. When Share is empty (path shallower than the share
    ///     segment index), the full path is shown as-is. File-ID-input results retain their
    ///     <c>(ID N)</c> annotation; results with no UNC path (Phase 0 lookup failures) fall back
    ///     to the raw display name.
    /// </summary>
    /// <param name="result">The per-file result.</param>
    /// <returns>The value to write into the <c>File</c> column.</returns>
    private static string FormatFileForColumn(FileUserResult result)
    {
        if (string.IsNullOrEmpty(result.UncPath))
        {
            // Phase 0 lookup failed (no UNC path resolved) — show whatever the DisplayName is;
            // typically that's "File ID 42".
            return result.DisplayName;
        }

        // Normalize separators so forward-slash-form UNC input compares cleanly against the
        // backslash-form Share prefix produced by UncShareExtractor.
        var normalizedPath = result.UncPath.Replace('/', '\\');

        string relativePath;

        if (!string.IsNullOrEmpty(result.Share)
            && normalizedPath.StartsWith(result.Share, StringComparison.OrdinalIgnoreCase))
        {
            // Strip the share prefix and any leading separator, leaving everything below the
            // share — including any intermediate folders between the share and the filename.
            relativePath = normalizedPath[result.Share.Length..].TrimStart('\\');
        }
        else
        {
            // Path was too shallow for the configured share segment index — no share to strip.
            // Show the full path so the file is still identifiable.
            relativePath = normalizedPath;
        }

        return result.FileId.HasValue
            ? $"{relativePath} (ID {result.FileId})"
            : relativePath;
    }
}
