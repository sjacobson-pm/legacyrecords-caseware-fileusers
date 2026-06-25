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
///     Removes staff that belong to the configured CaseWare support team Active Directory group.
/// </summary>
internal class SupportUserFilter : ISupportUserFilter
{
    private readonly ConfigurationOptions options;
    private readonly ILogger<SupportUserFilter> logger;

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
    }

    /// <inheritdoc />
    public ICollection<Staff> RemoveSupportUsers(ICollection<Staff> staff)
    {
        ArgumentNullException.ThrowIfNull(staff);

        this.logger.LogInformation("Start removing CaseWare support users from the list of staff with access...");

        try
        {
            var supportUserPrincipalNames = this.GetSupportTeamUserPrincipalNames();

            foreach (var userPrincipalName in supportUserPrincipalNames)
            {
                var supportStaff = staff.SingleOrDefault(o =>
                    string.Equals(o.UserPrincipalName, userPrincipalName, StringComparison.InvariantCultureIgnoreCase));

                if (supportStaff != null)
                {
                    staff.Remove(supportStaff);
                }
            }
        }
        catch (Exception ex)
        {
            // if an error occurs, just log it and return the current list of staff so processing is
            // not held up
            this.logger.LogError(
                ex,
                "An error occurred getting the members of the {GroupName} AD group.",
                this.options.ActiveDirectory.CaseWareSupportTeamGroupName);
        }

        this.logger.LogInformation("End removing CaseWare support users from the list of staff with access...");

        return staff;
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
}
