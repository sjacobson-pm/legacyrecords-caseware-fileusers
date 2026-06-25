using System;
using System.Collections.Generic;
using System.Threading;
using CaseWare;
using LegacyRecordsCaseWareFileUsers.Exceptions;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlanteMoran.CaseWare;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Opens a local CaseWare file and reads the users assigned to its FILE security group, ensuring
///     protection is enabled and retrying on transient server-fault errors.
/// </summary>
internal class CaseWareFileUserRetriever : ICaseWareFileUserRetriever
{
    private readonly ConfigurationOptions options;
    private readonly ICaseWareIntegrationService caseWareIntegrationService;
    private readonly ILogger<CaseWareFileUserRetriever> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFileUserRetriever" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="caseWareIntegrationService">The PlanteMoran CaseWare integration service.</param>
    /// <param name="logger">The logger.</param>
    public CaseWareFileUserRetriever(
        IOptions<ConfigurationOptions> optionsAccessor,
        ICaseWareIntegrationService caseWareIntegrationService,
        ILogger<CaseWareFileUserRetriever> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(caseWareIntegrationService);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = optionsAccessor.Value;
        this.caseWareIntegrationService = caseWareIntegrationService;
        this.logger = logger;
    }

    /// <inheritdoc />
    public ICollection<string> GetFileSecurityGroupUserIdentifiers(string caseWareFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseWareFilePath);

        ICollection<string> users = new List<string>();

        var retrievedSuccessfully = Retry(
            attempt =>
            {
                this.logger.LogDebug("Trying to retrieve FILE security group users - attempt #{Attempt}...", attempt);

                this.RunWithCaseWareClient(
                    client =>
                    {
                        this.EnsureProtectionIsEnabled(client);
                        var securityGroups = this.caseWareIntegrationService.GetSecurityGroups(client);
                        users = this.GetUsersIfSecurityGroupExists(securityGroups, Constants.FileSecurityGroupName, client);
                    },
                    caseWareFilePath);
            },
            ex => ex.Message.Contains(this.options.CaseWare.ServerFaultExceptionSubstring),
            TimeSpan.FromSeconds(1),
            this.options.CaseWare.RetryRetrievingUsersMaximumAttempts);

        if (!retrievedSuccessfully)
        {
            throw new CaseWareFileUserRetrievalException(
                $"Unable to retrieve users from the CaseWare file due to a '{this.options.CaseWare.ServerFaultExceptionSubstring}' exception.");
        }

        return users;
    }

    private static bool Retry(Action<int> code, Func<Exception, bool> retryIfCode, TimeSpan retryInterval, int maxAttemptCount)
    {
        for (var attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    Thread.Sleep(retryInterval);
                }

                code(attempted + 1);

                return true;
            }
            catch (Exception ex)
            {
                if (!retryIfCode(ex))
                {
                    throw;
                }
            }
        }

        return false;
    }

    private void RunWithCaseWareClient(Action<CWClient> code, string caseWareFilePath)
    {
        var loginUserId = this.options.CaseWare.LoginUserId;
        var loginUserPassword = this.options.CaseWare.LoginUserPassword;

        CWClient? caseWareClient = null;

        try
        {
            this.logger.LogInformation("Opening CaseWare file {CaseWareFilePath}...", caseWareFilePath);
            caseWareClient = this.caseWareIntegrationService.OpenCaseWareFile(caseWareFilePath, loginUserId, loginUserPassword);

            code(caseWareClient);
        }
        finally
        {
            if (caseWareClient != null)
            {
                this.caseWareIntegrationService.CloseCaseWareFile(caseWareClient);
            }
        }
    }

    private void EnsureProtectionIsEnabled(CWClient client)
    {
        if (!this.caseWareIntegrationService.IsProtectionEnabled(client))
        {
            this.logger.LogInformation("Protection is disabled; enabling protection...");

            this.caseWareIntegrationService.EnableCaseWareFileProtectionSetup(
                client,
                this.options.CaseWare.LoginUserId,
                this.options.CaseWare.LoginUserPassword);
        }
    }

    private ICollection<string> GetUsersIfSecurityGroupExists(ICollection<string> securityGroups, string securityGroupName, CWClient client)
    {
        if (!securityGroups.Contains(securityGroupName))
        {
            this.logger.LogInformation("The {GroupName} security group does not exist on the file.", securityGroupName);

            return new List<string>();
        }

        this.logger.LogInformation("Getting all users in the {GroupName} security group...", securityGroupName);

        return this.caseWareIntegrationService.GetAllUsersInSecurityGroup(client, securityGroupName);
    }
}
