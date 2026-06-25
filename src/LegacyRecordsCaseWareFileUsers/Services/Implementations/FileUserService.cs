using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using LegacyRecordsCaseWareFileUsers.Data.Repositories;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Coordinates reading inputs, retrieving each file's FILE-group users, mapping them to staff,
///     removing support users, and writing the resulting spreadsheet. Errors are recorded per file
///     and per user so a single failure never aborts the run.
/// </summary>
internal class FileUserService : IFileUserService
{
    private readonly ConfigurationOptions options;
    private readonly ILogger<FileUserService> logger;
    private readonly IInputReader inputReader;
    private readonly IFilePathRepository filePathRepository;
    private readonly ICaseWareFileUserRetriever caseWareFileUserRetriever;
    private readonly IStaffRepository staffRepository;
    private readonly ISupportUserFilter supportUserFilter;
    private readonly IWorkspaceManager workspaceManager;
    private readonly IFileUserSpreadsheetWriter spreadsheetWriter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FileUserService" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="inputReader">Reads and classifies the inputs.</param>
    /// <param name="filePathRepository">Resolves UNC paths for file identifiers.</param>
    /// <param name="caseWareFileUserRetriever">Retrieves FILE-group users from a CaseWare file.</param>
    /// <param name="staffRepository">Maps CaseWare user identifiers to staff.</param>
    /// <param name="supportUserFilter">Removes support users from the mapped staff.</param>
    /// <param name="workspaceManager">Provides isolated per-file workspaces.</param>
    /// <param name="spreadsheetWriter">Writes the resulting spreadsheet.</param>
    public FileUserService(
        IOptions<ConfigurationOptions> optionsAccessor,
        ILogger<FileUserService> logger,
        IInputReader inputReader,
        IFilePathRepository filePathRepository,
        ICaseWareFileUserRetriever caseWareFileUserRetriever,
        IStaffRepository staffRepository,
        ISupportUserFilter supportUserFilter,
        IWorkspaceManager workspaceManager,
        IFileUserSpreadsheetWriter spreadsheetWriter)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(inputReader);
        ArgumentNullException.ThrowIfNull(filePathRepository);
        ArgumentNullException.ThrowIfNull(caseWareFileUserRetriever);
        ArgumentNullException.ThrowIfNull(staffRepository);
        ArgumentNullException.ThrowIfNull(supportUserFilter);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(spreadsheetWriter);

        this.options = optionsAccessor.Value;
        this.logger = logger;
        this.inputReader = inputReader;
        this.filePathRepository = filePathRepository;
        this.caseWareFileUserRetriever = caseWareFileUserRetriever;
        this.staffRepository = staffRepository;
        this.supportUserFilter = supportUserFilter;
        this.workspaceManager = workspaceManager;
        this.spreadsheetWriter = spreadsheetWriter;
    }

    /// <inheritdoc />
    public async Task RunAsync(string? inputFilesPath, string? outputPath, CancellationToken cancellationToken = default)
    {
        var items = this.inputReader.ReadInputs(inputFilesPath);

        if (items.Count == 0)
        {
            this.logger.LogWarning("No inputs were supplied; an empty spreadsheet will be produced.");
        }

        var results = new List<FileUserResult>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await this.ProcessItemAsync(item, cancellationToken).ConfigureAwait(false));
        }

        var resolvedOutputPath = this.ResolveOutputPath(outputPath);

        this.spreadsheetWriter.Write(results, resolvedOutputPath);

        this.logger.LogInformation("Processed {FileCount} file(s); spreadsheet written to {OutputPath}.", results.Count, resolvedOutputPath);
    }

    private async Task<FileUserResult> ProcessItemAsync(FileInputItem item, CancellationToken cancellationToken)
    {
        var uncPath = item.UncPath;
        string displayName;

        if (item.IsFileId)
        {
            uncPath = await this.filePathRepository.TryGetUncPathAsync(item.FileId!.Value, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(uncPath))
            {
                var notFoundResult = new FileUserResult($"File ID {item.FileId}", item.FileId, null);
                notFoundResult.AddError($"No known file was found in the database for ID {item.FileId}.");

                return notFoundResult;
            }

            displayName = $"{uncPath} (ID {item.FileId})";
        }
        else
        {
            displayName = uncPath!;
        }

        var result = new FileUserResult(displayName, item.FileId, uncPath);

        string? workspaceDirectory = null;

        try
        {
            workspaceDirectory = this.workspaceManager.CreateWorkspace();
            var localFilePath = this.workspaceManager.CopyFileToWorkspace(uncPath!, workspaceDirectory);

            var userIdentifiers = this.caseWareFileUserRetriever.GetFileSecurityGroupUserIdentifiers(localFilePath);

            if (userIdentifiers.Count == 0)
            {
                this.logger.LogInformation(
                    "No users found in the {GroupName} security group for {File}.",
                    Constants.FileSecurityGroupName,
                    displayName);

                return result;
            }

            var mappedStaff = await this.staffRepository.GetStaffByCaseWareUserIdentifiersAsync(userIdentifiers, cancellationToken)
                                        .ConfigureAwait(false);

            this.RecordUnmappedUsers(result, userIdentifiers, mappedStaff);

            var reportableStaff = this.supportUserFilter.RemoveSupportUsers(mappedStaff);

            foreach (var staff in reportableStaff.OrderBy(o => o.FullName))
            {
                result.Users.Add(new ReportedUser(staff.FullName, staff.Office));
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "An error occurred processing file {File}.", displayName);
            result.AddError($"Error processing file: {ex.Message}");
        }
        finally
        {
            if (workspaceDirectory != null)
            {
                this.workspaceManager.DeleteWorkspace(workspaceDirectory);
            }
        }

        return result;
    }

    private void RecordUnmappedUsers(FileUserResult result, ICollection<string> userIdentifiers, ICollection<Staff> mappedStaff)
    {
        var matchedIdentifiers = new HashSet<string>(mappedStaff.Select(o => o.CaseWareUserIdentifier), StringComparer.InvariantCultureIgnoreCase);

        foreach (var identifier in userIdentifiers)
        {
            if (!matchedIdentifiers.Contains(identifier))
            {
                this.logger.LogWarning("CaseWare user '{Identifier}' could not be mapped to a staff record.", identifier);
                result.AddError($"CaseWare user '{identifier}' could not be mapped to a staff record.");
            }
        }
    }

    private string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        if (!string.IsNullOrWhiteSpace(this.options.Output.FilePath))
        {
            return this.options.Output.FilePath;
        }

        var directory = string.IsNullOrWhiteSpace(this.options.Output.Directory) ? Directory.GetCurrentDirectory() : this.options.Output.Directory;

        var fileName = $"FileUsers_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return Path.Combine(directory, fileName);
    }
}
