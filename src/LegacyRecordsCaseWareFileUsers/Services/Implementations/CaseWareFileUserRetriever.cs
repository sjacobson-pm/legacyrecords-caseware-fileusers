using System;
using System.Collections.Generic;
using CaseWare;
using LegacyRecordsCaseWareFileUsers.Exceptions;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlanteMoran.CaseWare;
using Polly;
using Polly.Retry;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Opens a local CaseWare file and reads the users assigned to its FILE security group, ensuring
///     protection is enabled and retrying transient failures with a Polly resilience pipeline.
/// </summary>
internal class CaseWareFileUserRetriever : ICaseWareFileUserRetriever
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly ConfigurationOptions options;
    private readonly ICaseWareIntegrationService caseWareIntegrationService;
    private readonly ILogger<CaseWareFileUserRetriever> logger;
    private readonly ResiliencePipeline retryPipeline;

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
        this.retryPipeline = this.BuildRetryPipeline();
    }

    /// <inheritdoc />
    public ICollection<string> GetFileSecurityGroupUserIdentifiers(string caseWareFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseWareFilePath);

        try
        {
            return this.retryPipeline.Execute(() =>
            {
                ICollection<string> users = new List<string>();

                this.RunWithCaseWareClient(
                    client =>
                    {
                        this.EnsureProtectionIsEnabled(client);
                        var securityGroups = this.caseWareIntegrationService.GetSecurityGroups(client);
                        users = this.GetUsersIfSecurityGroupExists(securityGroups, Constants.FileSecurityGroupName, client);
                    },
                    caseWareFilePath);

                return users;
            });
        }
        catch (Exception ex)
        {
            throw new CaseWareFileUserRetrievalException($"Unable to retrieve users from the CaseWare file '{caseWareFilePath}'.", ex);
        }
    }

    private ResiliencePipeline BuildRetryPipeline()
    {
        // RetryRetrievingUsersMaximumAttempts is the total number of attempts; Polly counts retries.
        var maxRetryAttempts = this.options.CaseWare.RetryRetrievingUsersMaximumAttempts - 1;

        if (maxRetryAttempts < 1)
        {
            // a single attempt with no retries; Polly requires MaxRetryAttempts to be at least one
            return ResiliencePipeline.Empty;
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = maxRetryAttempts,
                Delay = RetryDelay,
                BackoffType = DelayBackoffType.Constant,
                OnRetry = args =>
                {
                    this.logger.LogDebug(
                        "Retrying FILE security group retrieval after a transient failure (retry {RetryAttempt}): {Error}",
                        args.AttemptNumber + 1,
                        args.Outcome.Exception?.Message);

                    return default;
                },
            })
            .Build();
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
