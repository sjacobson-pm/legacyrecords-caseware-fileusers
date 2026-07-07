using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using LegacyRecordsCaseWareFileUsers.Data.Repositories;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class FileUserServiceTests
{
    private readonly IInputReader inputReader = Substitute.For<IInputReader>();
    private readonly ICaseWareFilesForApplicationsRepository filesRepository = Substitute.For<ICaseWareFilesForApplicationsRepository>();
    private readonly ICaseWareFileUserRetriever retriever = Substitute.For<ICaseWareFileUserRetriever>();
    private readonly IStaffRepository staffRepository = Substitute.For<IStaffRepository>();
    private readonly ISupportUserFilter supportUserFilter = Substitute.For<ISupportUserFilter>();
    private readonly IWorkspaceManager workspaceManager = Substitute.For<IWorkspaceManager>();
    private readonly IFileUserSpreadsheetWriter spreadsheetWriter = Substitute.For<IFileUserSpreadsheetWriter>();
    private readonly IServiceScopeFactory serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IRunContext runContext = Substitute.For<IRunContext>();
    private readonly IRunJournal runJournal = Substitute.For<IRunJournal>();

    private IReadOnlyList<FileUserResult>? capturedResults;

    public FileUserServiceTests()
    {
        // each up-front lookup and each parallel CaseWare read resolves its dependencies from a scope
        var scope = Substitute.For<IServiceScope>();
        var scopedProvider = Substitute.For<IServiceProvider>();
        this.serviceScopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(scopedProvider);
        scopedProvider.GetService(typeof(ICaseWareFilesForApplicationsRepository)).Returns(this.filesRepository);
        scopedProvider.GetService(typeof(IStaffRepository)).Returns(this.staffRepository);
        scopedProvider.GetService(typeof(ICaseWareFileUserRetriever)).Returns(this.retriever);

        // default lookups: no files resolve by ID and the staff query (if it runs at all) is empty
        this.filesRepository.GetUncPathsByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>()));
        this.staffRepository.GetStaffByIdentifiersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ICollection<Staff>>(new List<Staff>()));

        this.workspaceManager.CreateWorkspace().Returns(@"C:\ws");

        // copy is a passthrough: the "local" path used downstream is the source path
        this.workspaceManager.CopyFileToWorkspace(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => (string)ci[0]!);

        // by default the support filter removes nothing
        this.supportUserFilter.RemoveSupportUsers(Arg.Any<ICollection<Staff>>()).Returns(ci => ci.ArgAt<ICollection<Staff>>(0));

        this.spreadsheetWriter.When(w => w.Write(Arg.Any<IReadOnlyList<FileUserResult>>(), Arg.Any<string>()))
            .Do(ci => this.capturedResults = ci.ArgAt<IReadOnlyList<FileUserResult>>(0));

        this.runContext.RunId.Returns("test-swift-otter-runs");
        this.runContext.EffectiveOutputDirectory.Returns(@"C:\test-output");

        // default: empty journal (no resume state)
        this.runJournal.LoadForResumeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, JournalEntry>>(
                new Dictionary<string, JournalEntry>(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_PathInput_MapsUsersAndRecordsUnmappedAndRemovesSupport()
    {
        // Arrange
        this.GivenInputs(new FileInputItem(@"\\srv\a.ac_", null, @"\\srv\a.ac_"));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\a.ac_").Returns(new List<string> { "AAA", "BBB", "CCC" });

        var alice = MakeStaff("AAA", "Alice Adams", "Detroit", "Senior Consultant");
        var bob = MakeStaff("BBB", "Bob Brown", "Chicago", "Partner");

        // the bounded staff query returns only the identifiers that were seen
        this.staffRepository.GetStaffByIdentifiersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ICollection<Staff>>(new List<Staff> { alice, bob }));

        // remove Bob as a support user; CCC was never mapped to staff
        this.supportUserFilter.RemoveSupportUsers(Arg.Any<ICollection<Staff>>()).Returns(new List<Staff> { alice });

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.Users.ShouldHaveSingleItem();
        result.Users[0].FullName.ShouldBe("Alice Adams");
        result.Users[0].Office.ShouldBe("Detroit");
        result.Users[0].Position.ShouldBe("Senior Consultant");
        result.Errors.ShouldContain(e => e.Contains("CCC"));
    }

    [Fact]
    public async Task RunAsync_FileIdInput_ResolvesUncPathFromBatchedLookup()
    {
        // Arrange
        this.GivenInputs(new FileInputItem("42", 42, null));
        this.filesRepository.GetUncPathsByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string> { [42] = @"\\srv\byid.ac_" }));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\byid.ac_").Returns(new List<string>());

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert (the IDs are resolved in a single batched query)
        await this.filesRepository.Received(1).GetUncPathsByIdsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.Contains(42)),
            Arg.Any<CancellationToken>());
        this.retriever.Received(1).GetFileSecurityGroupUserIdentifiers(@"\\srv\byid.ac_");
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.UncPath.ShouldBe(@"\\srv\byid.ac_");
    }

    [Fact]
    public async Task RunAsync_FileIdNotFound_RecordsFileErrorAndDoesNotOpenCaseWare()
    {
        // Arrange (the batched lookup returns no path for ID 99)
        this.GivenInputs(new FileInputItem("99", 99, null));

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.Errors.ShouldContain(e => e.Contains("99"));
        result.Users.ShouldBeEmpty();
        this.retriever.DidNotReceiveWithAnyArgs().GetFileSecurityGroupUserIdentifiers(default!);
    }

    [Fact]
    public async Task RunAsync_RetrieverThrows_RecordsFileErrorAndStillProcessesOtherFiles()
    {
        // Arrange
        this.GivenInputs(new FileInputItem(@"\\srv\bad.ac_", null, @"\\srv\bad.ac_"), new FileInputItem(@"\\srv\good.ac_", null, @"\\srv\good.ac_"));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\bad.ac_").Returns(_ => throw new InvalidOperationException("boom"));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\good.ac_").Returns(new List<string>());

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert (results preserve input order despite parallelism)
        this.capturedResults!.Count.ShouldBe(2);
        this.capturedResults[0].Errors.ShouldContain(e => e.Contains("boom"));
        this.capturedResults[1].Errors.ShouldBeEmpty();

        // per-file workspaces are always cleaned up, and the workspace root is prepared and torn down
        this.workspaceManager.Received(2).DeleteWorkspace(@"C:\ws");
        this.workspaceManager.Received(1).PrepareWorkspaceRoot();
        this.workspaceManager.Received(1).CleanUpWorkspaceRoot();
    }

    [Fact]
    public async Task RunAsync_QueriesStaffOnceWithUnionOfIdentifiersSeenAcrossAllFiles()
    {
        // Arrange (three files, overlapping identifiers; one bounded staff query)
        this.GivenInputs(
            new FileInputItem(@"\\srv\a.ac_", null, @"\\srv\a.ac_"),
            new FileInputItem(@"\\srv\b.ac_", null, @"\\srv\b.ac_"),
            new FileInputItem(@"\\srv\c.ac_", null, @"\\srv\c.ac_"));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\a.ac_").Returns(new List<string> { "AAA", "BBB" });
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\b.ac_").Returns(new List<string> { "BBB", "CCC" });
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\c.ac_").Returns(new List<string> { "AAA" });

        IReadOnlyCollection<string>? capturedIdentifiers = null;
        this.staffRepository.GetStaffByIdentifiersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedIdentifiers = ci.ArgAt<IReadOnlyCollection<string>>(0);
                return Task.FromResult<ICollection<Staff>>(new List<Staff>());
            });

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert: exactly one staff query, with the deduplicated union of identifiers
        await this.staffRepository.Received(1).GetStaffByIdentifiersAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>());
        capturedIdentifiers.ShouldNotBeNull();
        capturedIdentifiers.OrderBy(s => s).ShouldBe(new[] { "AAA", "BBB", "CCC" });
    }

    [Fact]
    public async Task RunAsync_NoUsersAnyFile_SkipsStaffQueryEntirely()
    {
        // Arrange
        this.GivenInputs(new FileInputItem(@"\\srv\a.ac_", null, @"\\srv\a.ac_"));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\a.ac_").Returns(new List<string>());

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert: no identifiers were collected, so the staff database is never touched
        // (the `default` token below is just an NSubstitute argument placeholder for the
        // `WithAnyArgs` matcher and is never actually passed)
#pragma warning disable xUnit1051
        await this.staffRepository.DidNotReceiveWithAnyArgs().GetStaffByIdentifiersAsync(default!, default);
#pragma warning restore xUnit1051
    }

    [Fact]
    public async Task RunAsync_Resume_SkipsFilesAlreadyInJournal_AndDoesNotInvokePipeline()
    {
        // Two files in the input; the journal claims we already processed the first one. That file
        // should skip Phase 1 entirely (retriever + workspaceManager untouched for it) while its
        // identifiers still feed Phase 2 and land in the final spreadsheet.
        this.GivenInputs(
            new FileInputItem(@"\\srv\resumed.ac_", null, @"\\srv\resumed.ac_"),
            new FileInputItem(@"\\srv\fresh.ac_", null, @"\\srv\fresh.ac_"));

        var resumedKey = System.IO.Path.GetFullPath(@"\\srv\resumed.ac_").ToLowerInvariant();
        var resumedEntry = new JournalEntry
        {
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UncPath = @"\\srv\resumed.ac_",
            NormalizedKey = resumedKey,
            FileId = null,
            DisplayName = @"\\srv\resumed.ac_",
            UserIdentifiers = new[] { "AAA" },
            Errors = Array.Empty<string>(),
        };
        this.runJournal.LoadForResumeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, JournalEntry>>(
                new Dictionary<string, JournalEntry>(StringComparer.Ordinal) { [resumedKey] = resumedEntry }));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\fresh.ac_").Returns(new List<string> { "BBB" });

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert — retriever only invoked for the file NOT in the journal
        this.retriever.DidNotReceive().GetFileSecurityGroupUserIdentifiers(@"\\srv\resumed.ac_");
        this.retriever.Received(1).GetFileSecurityGroupUserIdentifiers(@"\\srv\fresh.ac_");

        // Result set includes both files in input order
        this.capturedResults!.Count.ShouldBe(2);
        this.capturedResults[0].DisplayName.ShouldBe(@"\\srv\resumed.ac_");
        this.capturedResults[1].DisplayName.ShouldBe(@"\\srv\fresh.ac_");
    }

    [Fact]
    public async Task RunAsync_Resume_DefaultRetrySetting_HonorsErroredJournalEntry()
    {
        // Default RetryErroredFilesOnResume = false — a journal entry with errors is trusted;
        // the file is not re-attempted and its errors appear in the output verbatim.
        this.GivenInputs(new FileInputItem(@"\\srv\errored.ac_", null, @"\\srv\errored.ac_"));

        var key = System.IO.Path.GetFullPath(@"\\srv\errored.ac_").ToLowerInvariant();
        var erroredEntry = new JournalEntry
        {
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UncPath = @"\\srv\errored.ac_",
            NormalizedKey = key,
            FileId = null,
            DisplayName = @"\\srv\errored.ac_",
            UserIdentifiers = Array.Empty<string>(),
            Errors = new[] { "prior run: could not open CaseWare file" },
        };
        this.runJournal.LoadForResumeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, JournalEntry>>(
                new Dictionary<string, JournalEntry>(StringComparer.Ordinal) { [key] = erroredEntry }));

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert — retriever was NOT called (trust the journal); the prior error is preserved
        this.retriever.DidNotReceiveWithAnyArgs().GetFileSecurityGroupUserIdentifiers(default!);
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.Errors.ShouldContain(e => e.Contains("could not open CaseWare file"));
    }

    [Fact]
    public async Task RunAsync_Resume_WithRetryEnabled_ReprocessesErroredJournalEntry()
    {
        // RetryErroredFilesOnResume = true — a journal entry with errors is ignored on load; the
        // file goes through Phase 1 again and its fresh result replaces the prior error.
        this.GivenInputs(new FileInputItem(@"\\srv\errored.ac_", null, @"\\srv\errored.ac_"));

        var key = System.IO.Path.GetFullPath(@"\\srv\errored.ac_").ToLowerInvariant();
        var erroredEntry = new JournalEntry
        {
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UncPath = @"\\srv\errored.ac_",
            NormalizedKey = key,
            FileId = null,
            DisplayName = @"\\srv\errored.ac_",
            UserIdentifiers = Array.Empty<string>(),
            Errors = new[] { "prior run: could not open CaseWare file" },
        };
        this.runJournal.LoadForResumeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, JournalEntry>>(
                new Dictionary<string, JournalEntry>(StringComparer.Ordinal) { [key] = erroredEntry }));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\errored.ac_").Returns(new List<string> { "AAA" });

        var sut = this.CreateSut(retryErroredFilesOnResume: true);

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert — retriever ran again and the prior error is no longer part of the result
        this.retriever.Received(1).GetFileSecurityGroupUserIdentifiers(@"\\srv\errored.ac_");
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.Errors.ShouldNotContain(e => e.Contains("could not open CaseWare file"));
    }

    [Fact]
    public async Task RunAsync_Resume_MarkCompletedAsyncFires_AfterSuccessfulPhase3()
    {
        // The journal must be renamed to .completed.jsonl once Phase 3 finishes so a subsequent
        // --resume against this RunID fails loudly rather than silently re-honoring the completed
        // record.
        this.GivenInputs(new FileInputItem(@"\\srv\a.ac_", null, @"\\srv\a.ac_"));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\a.ac_").Returns(new List<string>());

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        await this.runJournal.Received(1).MarkCompletedAsync();
    }

    [Fact]
    public async Task RunAsync_AppendsJournalEntry_ForEveryFileThatReachesPhase1()
    {
        // Every file that flows through Phase 1 — success or per-file error — should have its
        // outcome durably appended to the journal.
        this.GivenInputs(
            new FileInputItem(@"\\srv\good.ac_", null, @"\\srv\good.ac_"),
            new FileInputItem(@"\\srv\bad.ac_", null, @"\\srv\bad.ac_"));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\good.ac_").Returns(new List<string> { "AAA" });
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\bad.ac_")
            .Returns<ICollection<string>>(_ => throw new InvalidOperationException("boom"));

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert — one journal entry per file, whether it succeeded or errored
        await this.runJournal.Received(2).AppendAsync(Arg.Any<JournalEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ProcessesFilesConcurrently_UpToTheConfiguredDegree()
    {
        // Arrange
        const int degreeOfParallelism = 4;
        const int itemCount = 12;

        var items = Enumerable.Range(0, itemCount)
            .Select(i => new FileInputItem($@"\\srv\f{i}.ac_", null, $@"\\srv\f{i}.ac_"))
            .ToList();
        this.inputReader.ReadInputs(Arg.Any<string?>()).Returns(items);

        // make sure the thread pool can immediately supply enough threads for the blocking workers
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minCompletionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(minWorkerThreads, degreeOfParallelism + 2), minCompletionPortThreads);

        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;

        // a barrier sized to the configured degree only releases once that many workers are in flight
        // together, which proves real concurrency; the timeout guards against a hang
        using var barrier = new Barrier(degreeOfParallelism);

        this.retriever.GetFileSecurityGroupUserIdentifiers(Arg.Any<string>()).Returns(_ =>
        {
            var running = Interlocked.Increment(ref currentConcurrency);
            InterlockedMax(ref maxObservedConcurrency, running);

            barrier.SignalAndWait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Interlocked.Decrement(ref currentConcurrency);

            return new List<string>();
        });

        var sut = this.CreateSut(degreeOfParallelism);

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert: work ran exactly degree-at-a-time — never more (the cap is honored) and never
        // fewer (the files are genuinely processed in parallel)
        maxObservedConcurrency.ShouldBe(degreeOfParallelism);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;

        do
        {
            snapshot = Volatile.Read(ref target);

            if (value <= snapshot)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
    }

    private static Staff MakeStaff(string identifier, string fullName, string office, string position = "Staff")
    {
        return new Staff
        {
            CaseWareUserIdentifier = identifier,
            FullName = fullName,
            Office = office,
            UserPrincipalName = $"{fullName}@plantemoran.com",
            EmailAddress = $"{fullName}@plantemoran.com",
            Position = position,
            SamAccountName = fullName.Replace(" ", string.Empty),
        };
    }

    private void GivenInputs(params FileInputItem[] items)
    {
        this.inputReader.ReadInputs(Arg.Any<string?>()).Returns(items.ToList());
    }

    private FileUserService CreateSut(int maxDegreeOfParallelism = 0, bool retryErroredFilesOnResume = false)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
        {
            Processing = new ProcessingOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                RetryErroredFilesOnResume = retryErroredFilesOnResume,
            },
        });
        var logger = Substitute.For<ILogger<FileUserService>>();

        return new FileUserService(
            options,
            logger,
            this.inputReader,
            this.workspaceManager,
            this.spreadsheetWriter,
            this.serviceScopeFactory,
            this.supportUserFilter,
            this.runContext,
            this.runJournal);
    }
}
