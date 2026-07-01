using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class FileUserSpreadsheetWriterTests
{
    [Fact]
    public void Write_GroupsUsersByFileAndIncludesErrors()
    {
        // Arrange
        var result = new FileUserResult(@"\\srv\a.ac_", null, @"\\srv\a.ac_");
        result.Users.Add(new ReportedUser("Alice Adams", "Detroit", "Senior Consultant"));
        result.AddError("CaseWare user 'CCC' could not be mapped to a staff record.");

        var logger = Substitute.For<ILogger<FileUserSpreadsheetWriter>>();
        var sut = new FileUserSpreadsheetWriter(logger);

        var outputPath = Path.Combine(Path.GetTempPath(), $"fileusers-test-{System.Guid.NewGuid():N}.xlsx");

        try
        {
            // Act
            sut.Write(new List<FileUserResult> { result }, outputPath);

            // Assert
            File.Exists(outputPath).ShouldBeTrue();

            using var workbook = new XLWorkbook(outputPath);
            var worksheet = workbook.Worksheet("File Users");
            var cellValues = worksheet.CellsUsed().Select(c => c.GetString()).ToList();

            cellValues.ShouldContain(@"File: \\srv\a.ac_");
            cellValues.ShouldContain("Full Name");
            cellValues.ShouldContain("Office");
            cellValues.ShouldContain("Position");
            cellValues.ShouldContain("Alice Adams");
            cellValues.ShouldContain("Detroit");
            cellValues.ShouldContain("Senior Consultant");
            cellValues.ShouldContain(v => v.Contains("CCC"));

            // Guards against style bleed across the reusable IXLStyle instances. If independent styles
            // ever share a single mutable reference again (as they would with IXLWorkbook.Style), one
            // of these will fail because e.g. the column-header style would inherit the file-header's
            // gray fill or the errors-header would end up bold-and-red on rows that should be plain.
            var fileHeaderCell = worksheet.Cell(1, 1); // "File: ..." row
            fileHeaderCell.Style.Font.Bold.ShouldBeTrue();
            fileHeaderCell.Style.Font.FontColor.ShouldNotBe(XLColor.Red);

            var columnHeaderCell = worksheet.Cell(2, 1); // "Full Name" row
            columnHeaderCell.Style.Font.Bold.ShouldBeTrue();
            columnHeaderCell.Style.Font.Italic.ShouldBeFalse();
            columnHeaderCell.Style.Font.FontColor.ShouldNotBe(XLColor.Red);
            columnHeaderCell.Style.Fill.BackgroundColor.ShouldNotBe(XLColor.LightGray);

            var dataCell = worksheet.Cell(3, 1); // "Alice Adams" — a plain data cell
            dataCell.Style.Font.Bold.ShouldBeFalse();
            dataCell.Style.Font.Italic.ShouldBeFalse();
            dataCell.Style.Font.FontColor.ShouldNotBe(XLColor.Red);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
