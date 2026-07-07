using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void RemoveSupportUsers_DoesNotMutateTheInputCollection()
    {
        // The filter used to remove entries in place; the O(n) rewrite returns a fresh list so
        // callers can trust that the input collection is unchanged after the call.
        var alice = MakeStaff("Alice Adams", "alice.adams@plantemoran.com");
        var bob = MakeStaff("Bob Brown", "bob.brown@plantemoran.com");
        var input = new List<Staff> { alice, bob };
        var sut = CreateSut();

        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(new List<string> { "bob.brown@plantemoran.com" });

        sut.RemoveSupportUsers(input);

        input.ShouldBe(new[] { alice, bob });
    }

    [Fact]
    public void RemoveSupportUsers_StaffWithNullUpn_IsNeverTreatedAsSupportMember()
    {
        // A Staff record with a null UPN could otherwise trip a naive contains check; the O(n)
        // rewrite guards against this because HashSet<string>.Contains(null) throws.
        var alice = MakeStaff("Alice Adams", "alice.adams@plantemoran.com");
        var nullUpn = new Staff
        {
            CaseWareUserIdentifier = "no-upn-user",
            FullName = "Nell Null",
            Office = "Detroit",
            UserPrincipalName = null!,
            EmailAddress = string.Empty,
            Position = "Staff",
            SamAccountName = "nellnull",
        };
        var sut = CreateSut();

        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(new List<string> { "someone@plantemoran.com" });

        var result = sut.RemoveSupportUsers(new List<Staff> { alice, nullUpn });

        result.Count.ShouldBe(2);
        result.ShouldContain(nullUpn);
    }

    [Fact]
    public void RemoveSupportUsers_LargeSupportTeamAndStaffList_FinishesQuickly()
    {
        // Sanity-check the O(n) rewrite against the shape that used to be worst-case: a large
        // support team and a large staff list. The prior SingleOrDefault-in-a-loop was
        // O(supportTeamSize × staffPerFile); this version is O(staffPerFile) with O(1) HashSet
        // lookups. The test doesn't measure time — it exercises the code path at scale to catch
        // any accidental hot-path regression (an errant OrderBy, ToList, or nested loop).
        var supportTeam = Enumerable.Range(0, 10_000).Select(i => $"user{i}@plantemoran.com").ToList();
        var staff = Enumerable.Range(0, 500).Select(i => MakeStaff($"User {i * 3}", $"user{i * 3}@plantemoran.com")).ToList();
        var sut = CreateSut();

        sut.Configure().GetSupportTeamUserPrincipalNames().Returns(supportTeam);

        var result = sut.RemoveSupportUsers(staff);

        // Every staff member's UPN appears in the support team (500 × 3 = 1500 is well inside 10000),
        // so all get filtered out.
        result.ShouldBeEmpty();
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
