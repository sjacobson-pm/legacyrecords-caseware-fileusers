using System;
using System.Collections.Generic;
using System.Diagnostics;
using CaseWare;
using LegacyRecordsCaseWareFileUsers.Exceptions;
using LegacyRecordsCaseWareFileUsers.Helpers;
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

        var fileLabel = LogPathFormatter.FormatForLog(caseWareFilePath);

        try
        {
            return this.retryPipeline.Execute(() =>
            {
                ICollection<string> users = new List<string>();

                this.RunWithCaseWareClient(
                    client =>
                    {
                        this.EnsureProtectionIsEnabled(client, fileLabel);
                        var securityGroups = this.caseWareIntegrationService.GetSecurityGroups(client);
                        users = this.GetUsersIfSecurityGroupExists(securityGroups, Constants.FileSecurityGroupName, client, fileLabel);
                    },
                    caseWareFilePath,
                    fileLabel);

                return users;
            });
        }
        catch (Exception ex)
        {
            throw new CaseWareFileUserRetrievalException($"Unable to retrieve users from the CaseWare file '{fileLabel}'.", ex);
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
              .AddRetry(
                   new RetryStrategyOptions
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

    private void RunWithCaseWareClient(Action<CWClient> code, string caseWareFilePath, string fileLabel)
    {
        var loginUserId = this.options.CaseWare.LoginUserId;
        var loginUserPassword = this.options.CaseWare.LoginUserPassword;

        CWClient? caseWareClient = null;

        // stopwatch brackets the entire CaseWare session (open → work → close). Log lines emit the
        // managed thread ID so overlapping sessions in Phase 1 are recognizable in the log stream —
        // if CaseWare COM sessions truly run in parallel, multiple "CW-SESSION start" lines will
        // appear before any "CW-SESSION end" line does; if they serialize, each start will be
        // followed by its matching end before the next start.
        var threadId = Environment.CurrentManagedThreadId;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            this.logger.LogInformation("CW-SESSION start {File} on thread {ThreadId}", fileLabel, threadId);

            caseWareClient = this.caseWareIntegrationService.OpenCaseWareFile(caseWareFilePath, loginUserId, loginUserPassword);

            code(caseWareClient);
        }
        finally
        {
            if (caseWareClient != null)
            {
                this.caseWareIntegrationService.CloseCaseWareFile(caseWareClient);
            }

            stopwatch.Stop();

            this.logger.LogInformation(
                "CW-SESSION end {File} on thread {ThreadId} after {ElapsedMs} ms",
                fileLabel,
                threadId,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private void EnsureProtectionIsEnabled(CWClient client, string fileLabel)
    {
        if (!this.caseWareIntegrationService.IsProtectionEnabled(client))
        {
            this.logger.LogInformation("Protection is disabled on {File}; enabling protection...", fileLabel);

            this.caseWareIntegrationService.EnableCaseWareFileProtectionSetup(
                client,
                this.options.CaseWare.LoginUserId,
                this.options.CaseWare.LoginUserPassword);
        }
    }

    private ICollection<string> GetUsersIfSecurityGroupExists(
        ICollection<string> securityGroups,
        string securityGroupName,
        CWClient client,
        string fileLabel)
    {
        if (!securityGroups.Contains(securityGroupName))
        {
            this.logger.LogInformation("The {GroupName} security group does not exist on {File}.", securityGroupName, fileLabel);

            return new List<string>();
        }

        this.logger.LogInformation("Getting all users in the {GroupName} security group of {File}...", securityGroupName, fileLabel);

        return this.caseWareIntegrationService.GetAllUsersInSecurityGroup(client, securityGroupName);
    }
}
