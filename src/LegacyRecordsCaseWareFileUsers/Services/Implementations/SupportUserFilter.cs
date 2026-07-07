using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Removes staff that belong to the configured CaseWare support team Active Directory group. The
///     AD lookup is performed lazily on the first call to <see cref="RemoveSupportUsers" /> and the
///     resulting membership set is cached for the lifetime of the instance, so the filter can be
///     reused across many files without re-querying AD.
///     <para>
///         The membership set is held as a <see cref="HashSet{T}" /> with a case-insensitive
///         comparer, so each per-file <see cref="RemoveSupportUsers" /> call is O(staffPerFile)
///         with O(1) support-team lookups — much cheaper than the earlier
///         O(supportTeamSize × staffPerFile) nested-loop check for large support teams or files
///         with many users.
///     </para>
/// </summary>
internal class SupportUserFilter : ISupportUserFilter
{
    private readonly ConfigurationOptions options;
    private readonly ILogger<SupportUserFilter> logger;
    private readonly Lazy<HashSet<string>> supportTeamUserPrincipalNames;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SupportUserFilter" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="logger">The logger.</param>
    public SupportUserFilter(IOptions<ConfigurationOptions> optionsAccessor, ILogger<SupportUserFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = optionsAccessor.Value;
        this.logger = logger;

        // ExecutionAndPublication (the default) makes the lazy thread-safe and guarantees the
        // initialization function runs at most once even when callers race on the first access
        this.supportTeamUserPrincipalNames = new Lazy<HashSet<string>>(this.LoadSupportTeamUserPrincipalNames);
    }

    /// <inheritdoc />
    public ICollection<Staff> RemoveSupportUsers(ICollection<Staff> staff)
    {
        ArgumentNullException.ThrowIfNull(staff);

        var supportSet = this.supportTeamUserPrincipalNames.Value;

        if (supportSet.Count == 0)
        {
            // either the AD lookup failed (already logged) or the support team is empty; nothing to
            // remove
            return staff;
        }

        // Single pass over the file's staff with an O(1) hash lookup per member. Filters into a
        // fresh list rather than mutating the input, which keeps this method's return contract
        // predictable regardless of what the caller does with the original collection afterwards.
        var filtered = new List<Staff>(staff.Count);

        foreach (var member in staff)
        {
            if (member.UserPrincipalName != null && supportSet.Contains(member.UserPrincipalName))
            {
                continue;
            }

            filtered.Add(member);
        }

        return filtered;
    }

    /// <summary>
    ///     Returns the lower-cased user principal names of the members of the configured CaseWare
    ///     support team Active Directory group.
    /// </summary>
    /// <returns>The support team members' user principal names.</returns>
    [ExcludeFromCodeCoverage(Justification = "Requires Active Directory access; cannot be unit tested.")]
    protected internal virtual ICollection<string> GetSupportTeamUserPrincipalNames()
    {
        var activeDirectoryOptions = this.options.ActiveDirectory;

        using var context = new PrincipalContext(ContextType.Domain, activeDirectoryOptions.DomainName);
        using var groupPrincipal = GroupPrincipal.FindByIdentity(context, activeDirectoryOptions.CaseWareSupportTeamGroupName);

        if (groupPrincipal == null)
        {
            throw new InvalidOperationException($"No group found with the name {activeDirectoryOptions.CaseWareSupportTeamGroupName}.");
        }

        return groupPrincipal.GetMembers(true)
                             .Select(o => o.UserPrincipalName)
                             .Where(userPrincipalName => !string.IsNullOrWhiteSpace(userPrincipalName))
                             .Select(userPrincipalName => userPrincipalName!.ToLowerInvariant())
                             .ToList();
    }

    private HashSet<string> LoadSupportTeamUserPrincipalNames()
    {
        this.logger.LogInformation(
            "Loading members of the {GroupName} Active Directory group...",
            this.options.ActiveDirectory.CaseWareSupportTeamGroupName);

        // InvariantCultureIgnoreCase matches the case-insensitive comparison the previous
        // SingleOrDefault predicate performed. GetSupportTeamUserPrincipalNames already lowercases
        // each entry, so the case-fold applies consistently on both sides of the Contains check.
        var comparer = StringComparer.InvariantCultureIgnoreCase;

        try
        {
            var members = this.GetSupportTeamUserPrincipalNames();

            this.logger.LogInformation(
                "Loaded {SupportTeamMemberCount} member(s) of the {GroupName} Active Directory group.",
                members.Count,
                this.options.ActiveDirectory.CaseWareSupportTeamGroupName);

            return new HashSet<string>(members, comparer);
        }
        catch (Exception ex)
        {
            // if the AD lookup fails, log it and treat the support team as empty so processing is not
            // held up; the empty result is cached so the failure is not retried on every file
            this.logger.LogError(
                ex,
                "An error occurred getting the members of the {GroupName} AD group; no support users will be removed.",
                this.options.ActiveDirectory.CaseWareSupportTeamGroupName);

            return new HashSet<string>(comparer);
        }
    }
}
