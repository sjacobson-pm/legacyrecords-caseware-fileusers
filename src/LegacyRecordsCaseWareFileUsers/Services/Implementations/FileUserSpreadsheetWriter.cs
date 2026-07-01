using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Writes per-file user results to an .xlsx spreadsheet using ClosedXML, grouped by file. Styles
///     are constructed once per workbook and reused for every cell that needs them, so a batch of
///     thousands of files does not allocate a fresh style object per cell.
/// </summary>
internal class FileUserSpreadsheetWriter : IFileUserSpreadsheetWriter
{
    private const string WorksheetName = "File Users";

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
        var styles = SpreadsheetStyles.Create();

        var row = 1;

        foreach (var result in results)
        {
            row = WriteFileBlock(worksheet, row, result, styles);

            // blank spacer row between files
            row++;
        }

        worksheet.Columns(1, 3).AdjustToContents();

        workbook.SaveAs(outputFilePath);

        this.logger.LogInformation("Finished writing spreadsheet for {FileCount} file(s).", results.Count);
    }

    private static int WriteFileBlock(IXLWorksheet worksheet, int row, FileUserResult result, SpreadsheetStyles styles)
    {
        var headerCell = worksheet.Cell(row, 1);
        headerCell.Value = $"File: {result.DisplayName}";
        headerCell.Style = styles.FileHeader;
        worksheet.Range(row, 1, row, 3).Merge();
        row++;

        worksheet.Cell(row, 1).Value = "Full Name";
        worksheet.Cell(row, 1).Style = styles.ColumnHeader;
        worksheet.Cell(row, 2).Value = "Office";
        worksheet.Cell(row, 2).Style = styles.ColumnHeader;
        worksheet.Cell(row, 3).Value = "Position";
        worksheet.Cell(row, 3).Style = styles.ColumnHeader;
        row++;

        if (result.Users.Count == 0)
        {
            var noUsersCell = worksheet.Cell(row, 1);
            noUsersCell.Value = "No users assigned.";
            noUsersCell.Style = styles.NoUsers;
            row++;
        }
        else
        {
            foreach (var user in result.Users)
            {
                worksheet.Cell(row, 1).Value = user.FullName;
                worksheet.Cell(row, 2).Value = user.Office;
                worksheet.Cell(row, 3).Value = user.Position;
                row++;
            }
        }

        if (result.Errors.Count > 0)
        {
            var errorsHeader = worksheet.Cell(row, 1);
            errorsHeader.Value = "Errors:";
            errorsHeader.Style = styles.ErrorsHeader;
            row++;

            foreach (var error in result.Errors)
            {
                var errorCell = worksheet.Cell(row, 1);
                errorCell.Value = error;
                errorCell.Style = styles.ErrorRow;
                worksheet.Range(row, 1, row, 3).Merge();
                row++;
            }
        }

        return row;
    }

    // Named IXLStyle instances built once per Write() call and reused across every cell that needs
    // them. Each style is derived from XLWorkbook.DefaultStyle — that static returns a fresh IXLStyle
    // wrapper on every access, so the five styles below stay independent (unlike IXLWorkbook.Style,
    // which returns a single mutable reference and bleeds mutations across accesses).
    //
    // Reusing the same IXLStyle for many cells also lets ClosedXML deduplicate the underlying style
    // key in the OpenXML output, which keeps large workbooks smaller on disk.
    private sealed class SpreadsheetStyles
    {
        private SpreadsheetStyles(
            IXLStyle fileHeader,
            IXLStyle columnHeader,
            IXLStyle noUsers,
            IXLStyle errorsHeader,
            IXLStyle errorRow)
        {
            this.FileHeader = fileHeader;
            this.ColumnHeader = columnHeader;
            this.NoUsers = noUsers;
            this.ErrorsHeader = errorsHeader;
            this.ErrorRow = errorRow;
        }

        public IXLStyle FileHeader { get; }

        public IXLStyle ColumnHeader { get; }

        public IXLStyle NoUsers { get; }

        public IXLStyle ErrorsHeader { get; }

        public IXLStyle ErrorRow { get; }

        public static SpreadsheetStyles Create()
        {
            var fileHeader = XLWorkbook.DefaultStyle;
            fileHeader.Font.Bold = true;
            fileHeader.Fill.BackgroundColor = XLColor.LightGray;

            var columnHeader = XLWorkbook.DefaultStyle;
            columnHeader.Font.Bold = true;

            var noUsers = XLWorkbook.DefaultStyle;
            noUsers.Font.Italic = true;

            var errorsHeader = XLWorkbook.DefaultStyle;
            errorsHeader.Font.Bold = true;
            errorsHeader.Font.FontColor = XLColor.Red;

            var errorRow = XLWorkbook.DefaultStyle;
            errorRow.Font.FontColor = XLColor.Red;

            return new SpreadsheetStyles(fileHeader, columnHeader, noUsers, errorsHeader, errorRow);
        }
    }
}
