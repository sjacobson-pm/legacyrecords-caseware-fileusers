using System;
using System.IO;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Creates and tears down isolated local workspaces for processing individual CaseWare files.
/// </summary>
internal class WorkspaceManager : IWorkspaceManager
{
    private const string WorkspaceFolderPrefix = "lr-caseware-fileusers";

    private readonly ConfigurationOptions options;
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

        this.options = optionsAccessor.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string CreateWorkspace()
    {
        var configuredRoot = this.options.Workspace.RootPath;
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? Path.GetTempPath() : configuredRoot;

        var workspaceDirectory = Path.Combine(root, WorkspaceFolderPrefix, Guid.NewGuid().ToString("N"));

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
}
