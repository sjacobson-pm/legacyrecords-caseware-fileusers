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
        result.Users.Add(new ReportedUser("Alice Adams", "Detroit"));
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
            cellValues.ShouldContain("Alice Adams");
            cellValues.ShouldContain("Detroit");
            cellValues.ShouldContain(v => v.Contains("CCC"));
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
