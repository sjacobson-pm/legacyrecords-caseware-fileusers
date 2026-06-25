using System.Collections.Generic;
using System.Linq;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class InputReaderTests
{
    [Fact]
    public void ParseItems_IntegerLine_IsClassifiedAsFileId()
    {
        // Arrange
        var rawValues = new[] { "123" };

        // Act
        var items = InputReader.ParseItems(rawValues);

        // Assert
        var item = items.ShouldHaveSingleItem();
        item.IsFileId.ShouldBeTrue();
        item.FileId.ShouldBe(123);
        item.UncPath.ShouldBeNull();
    }

    [Fact]
    public void ParseItems_NonIntegerLine_IsClassifiedAsUncPath()
    {
        // Arrange
        var rawValues = new[] { @"\\server\share\engagement.ac_" };

        // Act
        var items = InputReader.ParseItems(rawValues);

        // Assert
        var item = items.ShouldHaveSingleItem();
        item.IsFileId.ShouldBeFalse();
        item.FileId.ShouldBeNull();
        item.UncPath.ShouldBe(@"\\server\share\engagement.ac_");
    }

    [Fact]
    public void ParseItems_BlankAndWhitespaceLines_AreSkipped()
    {
        // Arrange
        var rawValues = new[] { string.Empty, "   ", "\t" };

        // Act
        var items = InputReader.ParseItems(rawValues);

        // Assert
        items.ShouldBeEmpty();
    }

    [Fact]
    public void ParseItems_MixedLines_AreClassifiedAndTrimmedInOrder()
    {
        // Arrange
        var rawValues = new List<string>
        {
            " 45 ",
            @"\\srv\a.ac_",
            string.Empty,
            "67x",
            "8"
        };

        // Act
        var items = InputReader.ParseItems(rawValues).ToList();

        // Assert
        items.Count.ShouldBe(4);

        items[0].FileId.ShouldBe(45);
        items[1].UncPath.ShouldBe(@"\\srv\a.ac_");
        items[2].IsFileId.ShouldBeFalse();
        items[2].UncPath.ShouldBe("67x");
        items[3].FileId.ShouldBe(8);
    }
}
