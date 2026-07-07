using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class RunJournalTests
{
    [Fact]
    public async Task AppendThenLoad_RoundTripsEntries()
    {
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            var alpha = MakeEntry("alpha", @"\\srv\alpha.ac_", "AAA", "BBB");
            var beta = MakeEntry("beta", @"\\srv\beta.ac_", "CCC");

            await journal.AppendAsync(alpha, TestContext.Current.CancellationToken);
            await journal.AppendAsync(beta, TestContext.Current.CancellationToken);

            var loaded = await journal.LoadForResumeAsync(TestContext.Current.CancellationToken);

            loaded.Count.ShouldBe(2);
            loaded[alpha.NormalizedKey].UserIdentifiers.ShouldBe(new[] { "AAA", "BBB" });
            loaded[beta.NormalizedKey].UserIdentifiers.ShouldBe(new[] { "CCC" });
        });
    }

    [Fact]
    public async Task LoadForResume_WhenJournalMissing_ReturnsEmptyAndCreatesTheFile()
    {
        // Eager file creation is the fix for the "run crashed before any file completed Phase 1"
        // scenario: a subsequent --resume against this RunID must find a valid (empty) journal,
        // not fail loudly on missing-journal validation. Symmetric with how Serilog opens the log
        // file eagerly at run startup.
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            File.Exists(journal.JournalPath).ShouldBeFalse();

            var loaded = await journal.LoadForResumeAsync(TestContext.Current.CancellationToken);

            loaded.ShouldBeEmpty();
            File.Exists(journal.JournalPath).ShouldBeTrue();
        });
    }

    [Fact]
    public async Task LoadForResume_DuplicateKey_LastWins()
    {
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            var early = MakeEntry("alpha", @"\\srv\alpha.ac_", "AAA") with
            {
                Errors = new[] { "prior error" },
            };
            var late = MakeEntry("alpha", @"\\srv\alpha.ac_", "AAA", "BBB");

            await journal.AppendAsync(early, TestContext.Current.CancellationToken);
            await journal.AppendAsync(late, TestContext.Current.CancellationToken);

            var loaded = await journal.LoadForResumeAsync(TestContext.Current.CancellationToken);

            var entry = loaded.ShouldHaveSingleItem().Value;
            entry.UserIdentifiers.ShouldBe(new[] { "AAA", "BBB" });
            entry.Errors.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task LoadForResume_PartialLastLine_IsSkippedWithWarning()
    {
        // Simulates the hard-kill signature: valid line + valid line + truncated JSON (crash
        // mid-flush). Load should tolerate it and return the earlier well-formed entries.
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            var first = MakeEntry("alpha", @"\\srv\alpha.ac_", "AAA");
            var second = MakeEntry("beta", @"\\srv\beta.ac_", "BBB");

            await journal.AppendAsync(first, TestContext.Current.CancellationToken);
            await journal.AppendAsync(second, TestContext.Current.CancellationToken);

            // append a truncated JSON fragment to simulate a crash mid-write
            await File.AppendAllTextAsync(journal.JournalPath, "{\"timestamp\":\"2026-", TestContext.Current.CancellationToken);

            var loaded = await journal.LoadForResumeAsync(TestContext.Current.CancellationToken);

            loaded.Count.ShouldBe(2);
            loaded[first.NormalizedKey].UserIdentifiers.ShouldBe(new[] { "AAA" });
            loaded[second.NormalizedKey].UserIdentifiers.ShouldBe(new[] { "BBB" });
        });
    }

    [Fact]
    public async Task AppendAsync_ConcurrentCalls_AllEntriesWritten()
    {
        // Phase 1 workers run in parallel — the journal must serialize its own writes so nothing is
        // dropped or corrupted when multiple threads append at once.
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            const int concurrency = 20;

            var entries = Enumerable.Range(0, concurrency)
                .Select(i => MakeEntry($"key{i}", $@"\\srv\file{i}.ac_", $"USR{i}"))
                .ToList();

            await Task.WhenAll(entries.Select(e => journal.AppendAsync(e, TestContext.Current.CancellationToken)));

            var loaded = await journal.LoadForResumeAsync(TestContext.Current.CancellationToken);

            loaded.Count.ShouldBe(concurrency);
        });
    }

    [Fact]
    public async Task MarkCompletedAsync_RenamesTheJournalFile()
    {
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            var entry = MakeEntry("alpha", @"\\srv\alpha.ac_", "AAA");
            await journal.AppendAsync(entry, TestContext.Current.CancellationToken);

            File.Exists(journal.JournalPath).ShouldBeTrue();

            await journal.MarkCompletedAsync();

            File.Exists(journal.JournalPath).ShouldBeFalse();
            File.Exists(journal.CompletedJournalPath).ShouldBeTrue();
        });
    }

    [Fact]
    public async Task MarkCompletedAsync_WhenNoEntriesWritten_IsAlreadyNoOp()
    {
        await RunWithTempDirectoryAsync(async (directory, journal) =>
        {
            // no AppendAsync calls
            await journal.MarkCompletedAsync();

            File.Exists(journal.JournalPath).ShouldBeFalse();
            File.Exists(journal.CompletedJournalPath).ShouldBeFalse();
        });
    }

    private static JournalEntry MakeEntry(string keyStem, string uncPath, params string[] userIdentifiers)
    {
        return new JournalEntry
        {
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UncPath = uncPath,
            NormalizedKey = keyStem, // in real code this is Path.GetFullPath(...).ToLowerInvariant(); tests use a distinct stem to keep intent clear
            FileId = null,
            DisplayName = uncPath,
            UserIdentifiers = userIdentifiers,
            Errors = Array.Empty<string>(),
        };
    }

    private static async Task RunWithTempDirectoryAsync(Func<string, RunJournal, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var runContext = Substitute.For<IRunContext>();
            runContext.RunId.Returns("test-swift-otter-runs");
            runContext.EffectiveOutputDirectory.Returns(directory);

            var logger = Substitute.For<ILogger<RunJournal>>();
            using var journal = new RunJournal(runContext, logger);

            await test(directory, journal);
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
