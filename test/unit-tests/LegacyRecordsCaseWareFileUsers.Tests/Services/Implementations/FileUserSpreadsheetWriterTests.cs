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
    public void Write_ProducesFlatTable_WithHeaderRow_ShareColumn_AndAutoFilter()
    {
        // Share column holds the share prefix (\\server\volume\share-segment); File column holds
        // everything below it. Concatenating Share + "\" + File reconstructs the original UNC
        // path regardless of intermediate depth.
        var alpha = new FileUserResult(
            displayName: @"\\network\volume\alpha\engagement1\file.ac_",
            fileId: null,
            uncPath: @"\\network\volume\alpha\engagement1\file.ac_",
            share: @"\\network\volume\alpha");
        alpha.Users.Add(new ReportedUser("Alice Adams", "Detroit", "Senior Consultant"));
        alpha.Users.Add(new ReportedUser("Bob Brown", "Chicago", "Partner"));

        var beta = new FileUserResult(
            displayName: @"\\network\volume\beta\engagement2\file.ac_",
            fileId: null,
            uncPath: @"\\network\volume\beta\engagement2\file.ac_",
            share: @"\\network\volume\beta");
        beta.Users.Add(new ReportedUser("Carol Chen", "Detroit", "Manager"));
        beta.AddError("CaseWare user 'ZZZ' could not be mapped to a staff record.");

        var gammaEmpty = new FileUserResult(
            displayName: @"\\network\volume\gamma\engagement3\file.ac_",
            fileId: null,
            uncPath: @"\\network\volume\gamma\engagement3\file.ac_",
            share: @"\\network\volume\gamma");

        // File-ID-input file that resolved to a path — File column shows relative path plus "(ID N)"
        var withId = new FileUserResult(
            displayName: @"\\network\volume\delta\engagement4\file.ac_ (ID 99)",
            fileId: 99,
            uncPath: @"\\network\volume\delta\engagement4\file.ac_",
            share: @"\\network\volume\delta");
        withId.Users.Add(new ReportedUser("Dana Diaz", "New York", "Staff"));

        // Deep path: intermediate folders between the share prefix and the file must land in the
        // File column so reconstructability is preserved for any depth.
        var deep = new FileUserResult(
            displayName: @"\\network\volume\epsilon\extra\intermediate\engagement5\file.ac_",
            fileId: null,
            uncPath: @"\\network\volume\epsilon\extra\intermediate\engagement5\file.ac_",
            share: @"\\network\volume\epsilon");
        deep.Users.Add(new ReportedUser("Erin Eastman", "Boston", "Consultant"));

        // File-ID-input file that failed Phase 0 lookup — no UncPath, no Share.
        var deltaError = new FileUserResult("File ID 42", 42, null, string.Empty);
        deltaError.AddError("No known file was found in the database for ID 42.");

        RunWithTempOutput(outputPath =>
        {
            NewSut().Write(new List<FileUserResult> { alpha, beta, gammaEmpty, withId, deep, deltaError }, outputPath);

            using var workbook = new XLWorkbook(outputPath);
            var worksheet = workbook.Worksheet("File Users");

            // Header row is at row 1 and covers the six expected columns.
            worksheet.Cell(1, 1).GetString().ShouldBe("Share");
            worksheet.Cell(1, 2).GetString().ShouldBe("File");
            worksheet.Cell(1, 3).GetString().ShouldBe("Full Name");
            worksheet.Cell(1, 4).GetString().ShouldBe("Office");
            worksheet.Cell(1, 5).GetString().ShouldBe("Position");
            worksheet.Cell(1, 6).GetString().ShouldBe("Errors");

            // Data rows: alpha 2 users; beta 1 user + 1 error; gammaEmpty 1 placeholder;
            // withId 1 user; deep 1 user; deltaError 1 error row.
            var dataRows = CollectDataRows(worksheet);
            dataRows.Count.ShouldBe(8);

            // Share = share prefix; File = everything after it.
            dataRows[0].ShouldBe(new[] { @"\\network\volume\alpha", @"engagement1\file.ac_", "Alice Adams", "Detroit", "Senior Consultant", string.Empty });
            dataRows[1].ShouldBe(new[] { @"\\network\volume\alpha", @"engagement1\file.ac_", "Bob Brown", "Chicago", "Partner", string.Empty });
            dataRows[2].ShouldBe(new[] { @"\\network\volume\beta", @"engagement2\file.ac_", "Carol Chen", "Detroit", "Manager", string.Empty });

            // Error row for beta.
            dataRows[3][0].ShouldBe(@"\\network\volume\beta");
            dataRows[3][1].ShouldBe(@"engagement2\file.ac_");
            dataRows[3][2].ShouldBe(string.Empty);
            dataRows[3][5].ShouldContain("ZZZ");

            // Placeholder row for gammaEmpty.
            dataRows[4][0].ShouldBe(@"\\network\volume\gamma");
            dataRows[4][1].ShouldBe(@"engagement3\file.ac_");
            dataRows[4][5].ShouldContain("no users assigned");

            // File-ID-input with resolved path: File column has "(ID 99)" annotation appended.
            dataRows[5][0].ShouldBe(@"\\network\volume\delta");
            dataRows[5][1].ShouldBe(@"engagement4\file.ac_ (ID 99)");
            dataRows[5][2].ShouldBe("Dana Diaz");

            // Deep path: intermediate folders land in the File column, preserving reconstructability.
            dataRows[6][0].ShouldBe(@"\\network\volume\epsilon");
            dataRows[6][1].ShouldBe(@"extra\intermediate\engagement5\file.ac_");
            dataRows[6][2].ShouldBe("Erin Eastman");

            // Phase 0 failure: no UncPath → File column falls back to the raw display name.
            dataRows[7][0].ShouldBe(string.Empty);
            dataRows[7][1].ShouldBe("File ID 42");
            dataRows[7][5].ShouldContain("42");

            // The Excel Table is present and has autofilter enabled on the data range.
            var table = worksheet.Tables.First();
            table.ShowAutoFilter.ShouldBeTrue();
            table.Name.ShouldBe("FileUsers");
        });
    }

    [Fact]
    public void Write_EmptyResultSet_StillProducesUsableTable()
    {
        // A run with zero inputs (or every input filtered by resume) should still emit a valid
        // spreadsheet with header row and autofilter, so downstream tooling doesn't have to
        // special-case "empty batch" openings.
        RunWithTempOutput(outputPath =>
        {
            NewSut().Write(new List<FileUserResult>(), outputPath);

            using var workbook = new XLWorkbook(outputPath);
            var worksheet = workbook.Worksheet("File Users");

            worksheet.Cell(1, 1).GetString().ShouldBe("Share");
            worksheet.Cell(1, 6).GetString().ShouldBe("Errors");

            worksheet.Tables.Count().ShouldBe(1);
            worksheet.Tables.First().ShowAutoFilter.ShouldBeTrue();
        });
    }

    private static List<string[]> CollectDataRows(IXLWorksheet worksheet)
    {
        var rows = new List<string[]>();

        for (var rowNumber = 2; rowNumber <= worksheet.LastRowUsed()!.RowNumber(); rowNumber++)
        {
            var row = new string[6];

            for (var col = 1; col <= 6; col++)
            {
                row[col - 1] = worksheet.Cell(rowNumber, col).GetString();
            }

            rows.Add(row);
        }

        return rows;
    }

    private static FileUserSpreadsheetWriter NewSut()
    {
        var logger = Substitute.For<ILogger<FileUserSpreadsheetWriter>>();
        return new FileUserSpreadsheetWriter(logger);
    }

    private static void RunWithTempOutput(System.Action<string> test)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"fileusers-test-{System.Guid.NewGuid():N}.xlsx");

        try
        {
            test(outputPath);
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
