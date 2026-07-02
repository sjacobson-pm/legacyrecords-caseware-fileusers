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

    private FileUserService CreateSut(int maxDegreeOfParallelism = 0)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
        {
            Processing = new ProcessingOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
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
            this.runContext);
    }
}
