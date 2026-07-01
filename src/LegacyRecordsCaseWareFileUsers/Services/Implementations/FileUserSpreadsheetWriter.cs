using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Writes per-file user results to an .xlsx spreadsheet using ClosedXML, grouped by file.
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

        var row = 1;

        foreach (var result in results)
        {
            row = WriteFileBlock(worksheet, row, result);

            // blank spacer row between files
            row++;
        }

        worksheet.Columns(1, 3).AdjustToContents();

        workbook.SaveAs(outputFilePath);

        this.logger.LogInformation("Finished writing spreadsheet for {FileCount} file(s).", results.Count);
    }

    private static int WriteFileBlock(IXLWorksheet worksheet, int row, FileUserResult result)
    {
        var headerCell = worksheet.Cell(row, 1);
        headerCell.Value = $"File: {result.DisplayName}";
        headerCell.Style.Font.Bold = true;
        headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;
        worksheet.Range(row, 1, row, 3).Merge();
        row++;

        var fullNameHeader = worksheet.Cell(row, 1);
        fullNameHeader.Value = "Full Name";
        fullNameHeader.Style.Font.Bold = true;

        var officeHeader = worksheet.Cell(row, 2);
        officeHeader.Value = "Office";
        officeHeader.Style.Font.Bold = true;

        var positionHeader = worksheet.Cell(row, 3);
        positionHeader.Value = "Position";
        positionHeader.Style.Font.Bold = true;
        row++;

        if (result.Users.Count == 0)
        {
            var noUsersCell = worksheet.Cell(row, 1);
            noUsersCell.Value = "No users assigned.";
            noUsersCell.Style.Font.Italic = true;
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
            errorsHeader.Style.Font.Bold = true;
            errorsHeader.Style.Font.FontColor = XLColor.Red;
            row++;

            foreach (var error in result.Errors)
            {
                var errorCell = worksheet.Cell(row, 1);
                errorCell.Value = error;
                errorCell.Style.Font.FontColor = XLColor.Red;
                worksheet.Range(row, 1, row, 3).Merge();
                row++;
            }
        }

        return row;
    }
}
