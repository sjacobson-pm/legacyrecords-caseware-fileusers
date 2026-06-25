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
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class FileUserServiceTests
{
    private readonly IInputReader inputReader = Substitute.For<IInputReader>();
    private readonly IFilePathRepository filePathRepository = Substitute.For<IFilePathRepository>();
    private readonly ICaseWareFileUserRetriever retriever = Substitute.For<ICaseWareFileUserRetriever>();
    private readonly IStaffRepository staffRepository = Substitute.For<IStaffRepository>();
    private readonly ISupportUserFilter supportUserFilter = Substitute.For<ISupportUserFilter>();
    private readonly IWorkspaceManager workspaceManager = Substitute.For<IWorkspaceManager>();
    private readonly IFileUserSpreadsheetWriter spreadsheetWriter = Substitute.For<IFileUserSpreadsheetWriter>();

    private IReadOnlyList<FileUserResult>? capturedResults;

    public FileUserServiceTests()
    {
        this.workspaceManager.CreateWorkspace().Returns(@"C:\ws");

        // copy is a passthrough: the "local" path used downstream is the source path
        this.workspaceManager.CopyFileToWorkspace(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => (string)ci[0]!);

        // by default the support filter removes nothing
        this.supportUserFilter.RemoveSupportUsers(Arg.Any<ICollection<Staff>>()).Returns(ci => ci.ArgAt<ICollection<Staff>>(0));

        this.spreadsheetWriter.When(w => w.Write(Arg.Any<IReadOnlyList<FileUserResult>>(), Arg.Any<string>()))
            .Do(ci => this.capturedResults = ci.ArgAt<IReadOnlyList<FileUserResult>>(0));
    }

    [Fact]
    public async Task RunAsync_PathInput_MapsUsersAndRecordsUnmappedAndRemovesSupport()
    {
        // Arrange
        this.GivenInputs(new FileInputItem(@"\\srv\a.ac_", null, @"\\srv\a.ac_"));

        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\a.ac_").Returns(new List<string> { "AAA", "BBB", "CCC" });

        var alice = MakeStaff("AAA", "Alice Adams", "Detroit");
        var bob = MakeStaff("BBB", "Bob Brown", "Chicago");

        this.staffRepository.GetStaffByCaseWareUserIdentifiersAsync(Arg.Any<ICollection<string>>(), Arg.Any<CancellationToken>())
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
        result.Errors.ShouldContain(e => e.Contains("CCC"));
    }

    [Fact]
    public async Task RunAsync_FileIdInput_ResolvesUncPathFromRepository()
    {
        // Arrange
        this.GivenInputs(new FileInputItem("42", 42, null));
        this.filePathRepository.TryGetUncPathAsync(42, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(@"\\srv\byid.ac_"));
        this.retriever.GetFileSecurityGroupUserIdentifiers(@"\\srv\byid.ac_").Returns(new List<string>());

        var sut = this.CreateSut();

        // Act
        await sut.RunAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        await this.filePathRepository.Received(1).TryGetUncPathAsync(42, Arg.Any<CancellationToken>());
        this.retriever.Received(1).GetFileSecurityGroupUserIdentifiers(@"\\srv\byid.ac_");
        var result = this.capturedResults.ShouldHaveSingleItem();
        result.UncPath.ShouldBe(@"\\srv\byid.ac_");
    }

    [Fact]
    public async Task RunAsync_FileIdNotFound_RecordsFileErrorAndDoesNotOpenCaseWare()
    {
        // Arrange
        this.GivenInputs(new FileInputItem("99", 99, null));
        this.filePathRepository.TryGetUncPathAsync(99, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

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

        // Assert
        this.capturedResults!.Count.ShouldBe(2);
        this.capturedResults[0].Errors.ShouldContain(e => e.Contains("boom"));
        this.capturedResults[1].Errors.ShouldBeEmpty();

        // workspaces are always cleaned up
        this.workspaceManager.Received(2).DeleteWorkspace(@"C:\ws");
    }

    private static Staff MakeStaff(string identifier, string fullName, string office)
    {
        return new Staff
        {
            CaseWareUserIdentifier = identifier,
            FullName = fullName,
            Office = office,
            UserPrincipalName = $"{fullName}@plantemoran.com",
            EmailAddress = $"{fullName}@plantemoran.com",
            Position = "Staff",
            SamAccountName = fullName.Replace(" ", string.Empty),
        };
    }

    private void GivenInputs(params FileInputItem[] items)
    {
        this.inputReader.ReadInputs(Arg.Any<string?>()).Returns(items.ToList());
    }

    private FileUserService CreateSut()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions());
        var logger = Substitute.For<ILogger<FileUserService>>();

        return new FileUserService(
            options,
            logger,
            this.inputReader,
            this.filePathRepository,
            this.retriever,
            this.staffRepository,
            this.supportUserFilter,
            this.workspaceManager,
            this.spreadsheetWriter);
    }
}
