using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LegacyRecordsCaseWareFileUsers.Helpers;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Helpers;

public class RunIdGeneratorTests
{
    // A conservative pattern: three lowercase alphanumeric segments separated by two hyphens. The
    // exact word bank is validated by RunIdWordsTests; this just guards the composed shape.
    private static readonly Regex RunIdShape = new(@"^[a-z0-9]+-[a-z0-9]+-[a-z0-9]+$", RegexOptions.Compiled);

    [Fact]
    public void GenerateOne_ProducesTheExpectedShape()
    {
        for (var i = 0; i < 100; i++)
        {
            var runId = RunIdGenerator.GenerateOne();

            RunIdShape.IsMatch(runId).ShouldBeTrue($"Generated RunID '{runId}' does not match the expected shape.");

            var parts = runId.Split('-');
            parts.Length.ShouldBe(3);
            RunIdWords.Adjectives.ShouldContain(parts[0]);
            RunIdWords.Animals.ShouldContain(parts[1]);
            RunIdWords.Verbs.ShouldContain(parts[2]);
        }
    }

    [Fact]
    public void GenerateUnique_ReturnsAFreshIdWhenOutputDirectoryIsEmpty()
    {
        RunWithTempDirectory(directory =>
        {
            var runId = RunIdGenerator.GenerateUnique(directory);

            RunIdShape.IsMatch(runId).ShouldBeTrue();
        });
    }

    [Fact]
    public void GenerateUnique_ReturnsAFreshIdWhenOutputDirectoryDoesNotExist()
    {
        // a non-existent directory is treated as empty — any candidate is fine
        var directory = Path.Combine(Path.GetTempPath(), "runid-missing-" + Guid.NewGuid().ToString("N"));

        var runId = RunIdGenerator.GenerateUnique(directory);

        RunIdShape.IsMatch(runId).ShouldBeTrue();
    }

    [Fact]
    public void GenerateUnique_SkipsCandidatesThatAlreadyHaveArtifacts()
    {
        // preemptively occupy several candidate RunIDs by dropping their artifact files into the
        // directory, then confirm that GenerateUnique never returns one of those
        RunWithTempDirectory(directory =>
        {
            var occupied = Enumerable.Range(0, 5).Select(_ => RunIdGenerator.GenerateOne()).Distinct().ToArray();

            foreach (var runId in occupied)
            {
                File.WriteAllText(Path.Combine(directory, $"FileUsers-{runId}.journal.jsonl"), string.Empty);
            }

            // Given 7M+ unique combinations and only a handful occupied, the odds of ever colliding
            // are astronomically low; run enough times to make the test meaningful.
            for (var i = 0; i < 50; i++)
            {
                var runId = RunIdGenerator.GenerateUnique(directory);

                occupied.ShouldNotContain(runId);
            }
        });
    }

    private static void RunWithTempDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "runid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            test(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
