using System;
using System.IO;
using LegacyRecordsCaseWareFileUsers.Helpers;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Creates and tears down the local workspace root and the isolated per-file workspaces beneath
///     it. Cleanup operations are wrapped in a Polly retry pipeline because external processes
///     (antivirus scanners, file indexers, PDF viewers, etc.) commonly hold transient locks on files
///     just produced by CaseWare. A persistent failure is logged as a clean warning naming the
///     workspace and the underlying reason — never as a stack trace — so processing is not held up.
/// </summary>
internal class WorkspaceManager : IWorkspaceManager
{
    private const string WorkspaceFolderPrefix = "lr-caseware-fileusers";

    private static readonly ResiliencePipeline DirectoryDeletionPipeline = BuildDirectoryDeletionPipeline();

    private readonly string workspaceRoot;
    private readonly ILogger<WorkspaceManager> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WorkspaceManager" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="logger">The logger.</param>
    public WorkspaceManager(IOptions<ConfigurationOptions> optionsAccessor, ILogger<WorkspaceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        var configuredRoot = optionsAccessor.Value.Workspace.RootPath;
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? Path.GetTempPath() : configuredRoot;

        this.workspaceRoot = Path.Combine(root, WorkspaceFolderPrefix);
        this.logger = logger;
    }

    /// <inheritdoc />
    public void PrepareWorkspaceRoot()
    {
        Directory.CreateDirectory(this.workspaceRoot);

        this.logger.LogDebug("Prepared workspace root {WorkspaceRoot}.", LogPathFormatter.FormatForLog(this.workspaceRoot));
    }

    /// <inheritdoc />
    public string CreateWorkspace()
    {
        var workspaceDirectory = Path.Combine(this.workspaceRoot, Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workspaceDirectory);

        this.logger.LogDebug("Created workspace {Workspace}.", LogPathFormatter.FormatForLog(workspaceDirectory));

        return workspaceDirectory;
    }

    /// <inheritdoc />
    public string CopyFileToWorkspace(string sourceUncPath, string workspaceDirectory)
    {
        var fileName = Path.GetFileName(sourceUncPath);
        var destinationPath = Path.Combine(workspaceDirectory, fileName);

        this.logger.LogDebug(
            "Copying {Source} to {Destination}...",
            LogPathFormatter.FormatForLog(sourceUncPath),
            LogPathFormatter.FormatForLog(destinationPath));

        File.Copy(sourceUncPath, destinationPath, true);

        return destinationPath;
    }

    /// <inheritdoc />
    public void DeleteWorkspace(string workspaceDirectory)
    {
        if (!Directory.Exists(workspaceDirectory))
        {
            return;
        }

        var workspaceLabel = LogPathFormatter.FormatForLog(workspaceDirectory);

        try
        {
            DirectoryDeletionPipeline.Execute(() => Directory.Delete(workspaceDirectory, true));

            this.logger.LogDebug("Deleted workspace {Workspace}.", workspaceLabel);
        }
        catch (Exception ex)
        {
            // a cleanup failure should not stop processing; report a clean message (no stack trace)
            // naming the workspace and the underlying reason
            this.logger.LogWarning(
                "Could not delete workspace {Workspace} after retries: {Reason:l}. " +
                "The workspace will be left in place and can be removed manually.",
                workspaceLabel,
                ex.Message);
        }
    }

    /// <inheritdoc />
    public void CleanUpWorkspaceRoot()
    {
        if (!Directory.Exists(this.workspaceRoot))
        {
            return;
        }

        var rootLabel = LogPathFormatter.FormatForLog(this.workspaceRoot);

        try
        {
            DirectoryDeletionPipeline.Execute(() => Directory.Delete(this.workspaceRoot, true));

            this.logger.LogDebug("Deleted workspace root {WorkspaceRoot}.", rootLabel);
        }
        catch (Exception ex)
        {
            // a cleanup failure should not stop processing; report a clean message (no stack trace)
            // naming the workspace root and the underlying reason
            this.logger.LogWarning(
                "Could not delete workspace root {WorkspaceRoot} after retries: {Reason:l}. " +
                "The workspace root will be left in place and can be removed manually.",
                rootLabel,
                ex.Message);
        }
    }

    private static ResiliencePipeline BuildDirectoryDeletionPipeline()
    {
        // retry transient lock errors (IO/sharing violations, access denied) on the assumption that
        // an external process is holding the file briefly; exponential backoff so a longer-held
        // lock has a chance to release before we give up
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<IOException>().Handle<UnauthorizedAccessException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
            })
            .Build();
    }
}
