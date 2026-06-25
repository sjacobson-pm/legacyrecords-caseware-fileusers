using System;
using System.Collections.Generic;
using CaseWare;
using LegacyRecordsCaseWareFileUsers.Exceptions;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PlanteMoran.CaseWare;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class CaseWareFileUserRetrieverTests
{
    private const string FilePath = @"C:\ws\engagement.ac_";
    private const string LoginUserId = "login-user";
    private const string LoginUserPassword = "login-password";

    private readonly ICaseWareIntegrationService caseWareIntegrationService = Substitute.For<ICaseWareIntegrationService>();
    private readonly CWClient client = Substitute.For<CWClient>();

    public CaseWareFileUserRetrieverTests()
    {
        this.caseWareIntegrationService
            .OpenCaseWareFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(this.client);
        this.caseWareIntegrationService.IsProtectionEnabled(this.client).Returns(true);
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_FileGroupExists_ReturnsItsUsersAndOpensAndClosesFile()
    {
        // Arrange
        this.caseWareIntegrationService.GetSecurityGroups(this.client)
            .Returns(new List<string> { Constants.FileSecurityGroupName, "OTHER" });
        this.caseWareIntegrationService.GetAllUsersInSecurityGroup(this.client, Constants.FileSecurityGroupName)
            .Returns(new List<string> { "AAA", "BBB" });
        var sut = this.CreateSut();

        // Act
        var result = sut.GetFileSecurityGroupUserIdentifiers(FilePath);

        // Assert
        result.ShouldBe(new[] { "AAA", "BBB" });
        this.caseWareIntegrationService.Received(1).OpenCaseWareFile(FilePath, LoginUserId, LoginUserPassword);
        this.caseWareIntegrationService.Received(1).CloseCaseWareFile(this.client);
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_FileGroupMissing_ReturnsEmptyAndDoesNotQueryUsers()
    {
        // Arrange
        this.caseWareIntegrationService.GetSecurityGroups(this.client).Returns(new List<string> { "OTHER" });
        var sut = this.CreateSut();

        // Act
        var result = sut.GetFileSecurityGroupUserIdentifiers(FilePath);

        // Assert
        result.ShouldBeEmpty();
        this.caseWareIntegrationService.DidNotReceive().GetAllUsersInSecurityGroup(Arg.Any<CWClient>(), Arg.Any<string>());
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_ProtectionDisabled_EnablesProtection()
    {
        // Arrange
        this.caseWareIntegrationService.IsProtectionEnabled(this.client).Returns(false);
        this.caseWareIntegrationService.GetSecurityGroups(this.client).Returns(new List<string>());
        var sut = this.CreateSut();

        // Act
        sut.GetFileSecurityGroupUserIdentifiers(FilePath);

        // Assert
        this.caseWareIntegrationService.Received(1).EnableCaseWareFileProtectionSetup(this.client, LoginUserId, LoginUserPassword);
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_ProtectionEnabled_DoesNotEnableProtection()
    {
        // Arrange
        this.caseWareIntegrationService.GetSecurityGroups(this.client).Returns(new List<string>());
        var sut = this.CreateSut();

        // Act
        sut.GetFileSecurityGroupUserIdentifiers(FilePath);

        // Assert
        this.caseWareIntegrationService.DidNotReceive()
            .EnableCaseWareFileProtectionSetup(Arg.Any<CWClient>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_FailureWithNoRetries_ClosesFileAndThrowsRetrievalException()
    {
        // Arrange
        this.caseWareIntegrationService.GetSecurityGroups(this.client)
            .Returns<ICollection<string>>(_ => throw new InvalidOperationException("read failed"));
        var sut = this.CreateSut(maximumAttempts: 1);

        // Act / Assert
        Should.Throw<CaseWareFileUserRetrievalException>(() => sut.GetFileSecurityGroupUserIdentifiers(FilePath));
        this.caseWareIntegrationService.Received(1).CloseCaseWareFile(this.client);
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_TransientFailureThenSuccess_RetriesAndReturnsUsers()
    {
        // Arrange
        var attempt = 0;
        this.caseWareIntegrationService.GetSecurityGroups(this.client).Returns<ICollection<string>>(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new Exception("a transient failure");
            }

            return new List<string> { Constants.FileSecurityGroupName };
        });
        this.caseWareIntegrationService.GetAllUsersInSecurityGroup(this.client, Constants.FileSecurityGroupName)
            .Returns(new List<string> { "X" });
        var sut = this.CreateSut(maximumAttempts: 3);

        // Act
        var result = sut.GetFileSecurityGroupUserIdentifiers(FilePath);

        // Assert
        result.ShouldBe(new[] { "X" });
        this.caseWareIntegrationService.Received(2).OpenCaseWareFile(FilePath, LoginUserId, LoginUserPassword);
    }

    [Fact]
    public void GetFileSecurityGroupUserIdentifiers_FailureOnEveryAttempt_ThrowsRetrievalExceptionAfterExhaustingRetries()
    {
        // Arrange
        this.caseWareIntegrationService.GetSecurityGroups(this.client)
            .Returns<ICollection<string>>(_ => throw new Exception("persistent failure"));
        var sut = this.CreateSut(maximumAttempts: 2);

        // Act / Assert
        Should.Throw<CaseWareFileUserRetrievalException>(() => sut.GetFileSecurityGroupUserIdentifiers(FilePath));
        this.caseWareIntegrationService.Received(2).OpenCaseWareFile(FilePath, LoginUserId, LoginUserPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetFileSecurityGroupUserIdentifiers_BlankPath_Throws(string? path)
    {
        // Arrange
        var sut = this.CreateSut();

        // Act / Assert
        Should.Throw<ArgumentException>(() => sut.GetFileSecurityGroupUserIdentifiers(path!));
    }

    private CaseWareFileUserRetriever CreateSut(int maximumAttempts = 1)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
        {
            CaseWare = new CaseWareOptions
            {
                LoginUserId = LoginUserId,
                LoginUserPassword = LoginUserPassword,
                RetryRetrievingUsersMaximumAttempts = maximumAttempts,
            },
        });
        var logger = Substitute.For<ILogger<CaseWareFileUserRetriever>>();

        return new CaseWareFileUserRetriever(options, this.caseWareIntegrationService, logger);
    }
}
