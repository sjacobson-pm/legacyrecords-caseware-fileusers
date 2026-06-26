using System;
using System.IO;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Creates and tears down the local workspace root and the isolated per-file workspaces beneath
///     it.
/// </summary>
internal class WorkspaceManager : IWorkspaceManager
{
    private const string WorkspaceFolderPrefix = "lr-caseware-fileusers";

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

        this.logger.LogDebug("Prepared workspace root {WorkspaceRoot}.", this.workspaceRoot);
    }

    /// <inheritdoc />
    public string CreateWorkspace()
    {
        var workspaceDirectory = Path.Combine(this.workspaceRoot, Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workspaceDirectory);

        this.logger.LogDebug("Created workspace {WorkspaceDirectory}.", workspaceDirectory);

        return workspaceDirectory;
    }

    /// <inheritdoc />
    public string CopyFileToWorkspace(string sourceUncPath, string workspaceDirectory)
    {
        var fileName = Path.GetFileName(sourceUncPath);
        var destinationPath = Path.Combine(workspaceDirectory, fileName);

        this.logger.LogDebug("Copying {SourceUncPath} to {DestinationPath}...", sourceUncPath, destinationPath);

        File.Copy(sourceUncPath, destinationPath, true);

        return destinationPath;
    }

    /// <inheritdoc />
    public void DeleteWorkspace(string workspaceDirectory)
    {
        try
        {
            if (Directory.Exists(workspaceDirectory))
            {
                Directory.Delete(workspaceDirectory, true);
                this.logger.LogDebug("Deleted workspace {WorkspaceDirectory}.", workspaceDirectory);
            }
        }
        catch (Exception ex)
        {
            // cleanup failures should not stop processing; log and continue
            this.logger.LogWarning(ex, "Failed to delete workspace {WorkspaceDirectory}.", workspaceDirectory);
        }
    }

    /// <inheritdoc />
    public void CleanUpWorkspaceRoot()
    {
        try
        {
            if (Directory.Exists(this.workspaceRoot))
            {
                Directory.Delete(this.workspaceRoot, true);
                this.logger.LogDebug("Deleted workspace root {WorkspaceRoot}.", this.workspaceRoot);
            }
        }
        catch (Exception ex)
        {
            // cleanup failures should not stop processing; log and continue
            this.logger.LogWarning(ex, "Failed to delete workspace root {WorkspaceRoot}.", this.workspaceRoot);
        }
    }
}
