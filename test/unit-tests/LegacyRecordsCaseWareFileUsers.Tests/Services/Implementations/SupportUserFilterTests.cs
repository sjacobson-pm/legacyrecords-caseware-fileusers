using System;
using System.Collections.Generic;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Extensions;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class SupportUserFilterTests
{
    [Fact]
    public void RemoveSupportUsers_RemovesStaffMatchingSupportGroupByUserPrincipalName()
    {
        // Arrange
        var alice = MakeStaff("Alice Adams", "alice.adams@plantemoran.com");
        var bob = MakeStaff("Bob Brown", "Bob.Brown@PlanteMoran.com");
        var sut = CreateSut();

        // support group membership is returned lower-cased by the (stubbed) AD lookup
        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(new List<string> { "bob.brown@plantemoran.com" });

        // Act
        var result = sut.RemoveSupportUsers(new List<Staff> { alice, bob });

        // Assert (Bob matched case-insensitively and was removed)
        result.ShouldHaveSingleItem().ShouldBe(alice);
    }

    [Fact]
    public void RemoveSupportUsers_NoStaffInSupportGroup_KeepsAllStaff()
    {
        // Arrange
        var alice = MakeStaff("Alice Adams", "alice.adams@plantemoran.com");
        var bob = MakeStaff("Bob Brown", "bob.brown@plantemoran.com");
        var sut = CreateSut();

        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(new List<string> { "someone.else@plantemoran.com" });

        // Act
        var result = sut.RemoveSupportUsers(new List<Staff> { alice, bob });

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(alice);
        result.ShouldContain(bob);
    }

    [Fact]
    public void RemoveSupportUsers_AdLookupThrows_ReturnsStaffUnchanged()
    {
        // Arrange
        var alice = MakeStaff("Alice Adams", "alice.adams@plantemoran.com");
        var bob = MakeStaff("Bob Brown", "bob.brown@plantemoran.com");
        var sut = CreateSut();

        sut.Configure().GetSupportTeamUserPrincipalNames()
            .Returns<ICollection<string>>(_ => throw new InvalidOperationException("AD unavailable"));

        // Act
        var result = sut.RemoveSupportUsers(new List<Staff> { alice, bob });

        // Assert (error is swallowed so processing is not held up)
        result.Count.ShouldBe(2);
    }

    [Fact]
    public void RemoveSupportUsers_CalledForManyFiles_QueriesActiveDirectoryAtMostOnce()
    {
        // Arrange
        var sut = CreateSut();
        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(new List<string> { "someone@plantemoran.com" });

        // Act (simulate processing many files in a row)
        for (var i = 0; i < 10; i++)
        {
            sut.RemoveSupportUsers(new List<Staff> { MakeStaff($"User {i}", $"user{i}@plantemoran.com") });
        }

        // Assert: the AD lookup is cached for the lifetime of the instance, so it runs at most once
        sut.Received(1).GetSupportTeamUserPrincipalNames();
    }

    [Fact]
    public void RemoveSupportUsers_NullStaff_Throws()
    {
        // Arrange
        var sut = CreateSut();

        // Act / Assert
        Should.Throw<ArgumentNullException>(() => sut.RemoveSupportUsers(null!));
    }

    private static Staff MakeStaff(string fullName, string userPrincipalName)
    {
        return new Staff
        {
            CaseWareUserIdentifier = fullName.Replace(" ", string.Empty),
            FullName = fullName,
            Office = "Detroit",
            UserPrincipalName = userPrincipalName,
            EmailAddress = userPrincipalName,
            Position = "Staff",
            SamAccountName = fullName.Replace(" ", string.Empty),
        };
    }

    private static SupportUserFilter CreateSut()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationOptions
        {
            ActiveDirectory = new ActiveDirectoryOptions
            {
                DomainName = "plantemoran.com",
                CaseWareSupportTeamGroupName = "CaseWare Support",
            },
        });
        var logger = Substitute.For<ILogger<SupportUserFilter>>();

        return Substitute.ForPartsOf<SupportUserFilter>(options, logger);
    }
}
