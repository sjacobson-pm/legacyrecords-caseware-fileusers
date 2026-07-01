using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Microsoft.Extensions.Logging;
using NSubstitute;
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

    [Fact]
    public void ReadInputs_NoCliArgument_FallsBackToInputFilePathConfig()
    {
        // Arrange: a real text file referenced by Input:FilePath (no --input-files supplied)
        var tempInputFile = Path.Combine(Path.GetTempPath(), $"inputs-test-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(tempInputFile, new[] { @"\\srv\from-filepath.ac_", "42" });

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
            {
                Input = new InputOptions { FilePath = tempInputFile, Files = new List<string> { "ignored-when-filepath-is-set" } },
            });
            var sut = new InputReader(options, Substitute.For<ILogger<InputReader>>());

            // Act
            var items = sut.ReadInputs(null);

            // Assert: the FilePath file wins over the inline Files array
            items.Count.ShouldBe(2);
            items[0].UncPath.ShouldBe(@"\\srv\from-filepath.ac_");
            items[1].FileId.ShouldBe(42);
        }
        finally
        {
            File.Delete(tempInputFile);
        }
    }

    [Fact]
    public void ReadInputs_CliArgumentTakesPrecedenceOverInputFilePathConfig()
    {
        // Arrange: both a CLI argument and a config FilePath point at different files
        var cliFile = Path.Combine(Path.GetTempPath(), $"cli-inputs-{Guid.NewGuid():N}.txt");
        var configFile = Path.Combine(Path.GetTempPath(), $"config-inputs-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(cliFile, new[] { @"\\srv\from-cli.ac_" });
        File.WriteAllLines(configFile, new[] { @"\\srv\from-config.ac_" });

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
            {
                Input = new InputOptions { FilePath = configFile },
            });
            var sut = new InputReader(options, Substitute.For<ILogger<InputReader>>());

            // Act
            var items = sut.ReadInputs(cliFile);

            // Assert: the CLI argument wins
            items.ShouldHaveSingleItem().UncPath.ShouldBe(@"\\srv\from-cli.ac_");
        }
        finally
        {
            File.Delete(cliFile);
            File.Delete(configFile);
        }
    }
}
