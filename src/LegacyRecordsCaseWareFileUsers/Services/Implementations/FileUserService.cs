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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Coordinates reading inputs, retrieving each file's FILE-group users, mapping them to staff,
///     removing support users, and writing the resulting spreadsheet. The work is structured into
///     three phases so the database is hit at most twice per run, regardless of batch size, and so the
///     parallel per-file work never touches the database or Active Directory:
///     <para>
///         Phase 0 resolves file-ID inputs to UNC paths in a single bounded query. Phase 1 opens each
///         file in parallel (bounded by <see cref="ProcessingOptions.MaxDegreeOfParallelism" />) and
///         reads the FILE security group identifiers; no database work is done here. Phase 2 issues
///         one bounded staff query against the union of identifiers actually seen. Phase 3 maps the
///         results in memory and produces the per-file output rows.
///     </para>
///     <para>
///         Errors are recorded per file and per user so a single failure never aborts the run.
///     </para>
/// </summary>
internal class FileUserService : IFileUserService
{
    private readonly ConfigurationOptions options;
    private readonly ILogger<FileUserService> logger;
    private readonly IInputReader inputReader;
    private readonly IWorkspaceManager workspaceManager;
    private readonly IFileUserSpreadsheetWriter spreadsheetWriter;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly ISupportUserFilter supportUserFilter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FileUserService" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="inputReader">Reads and classifies the inputs.</param>
    /// <param name="workspaceManager">Manages the workspace root and per-file workspaces.</param>
    /// <param name="spreadsheetWriter">Writes the resulting spreadsheet.</param>
    /// <param name="serviceScopeFactory">Creates a dependency-injection scope per file so each
    /// parallel worker gets its own CaseWare session.</param>
    /// <param name="supportUserFilter">The support-user filter (singleton; caches the AD lookup for
    /// the lifetime of the run).</param>
    public FileUserService(
        IOptions<ConfigurationOptions> optionsAccessor,
        ILogger<FileUserService> logger,
        IInputReader inputReader,
        IWorkspaceManager workspaceManager,
        IFileUserSpreadsheetWriter spreadsheetWriter,
        IServiceScopeFactory serviceScopeFactory,
        ISupportUserFilter supportUserFilter)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(inputReader);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(spreadsheetWriter);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(supportUserFilter);

        this.options = optionsAccessor.Value;
        this.logger = logger;
        this.inputReader = inputReader;
        this.workspaceManager = workspaceManager;
        this.spreadsheetWriter = spreadsheetWriter;
        this.serviceScopeFactory = serviceScopeFactory;
        this.supportUserFilter = supportUserFilter;
    }

    /// <inheritdoc />
    public async Task RunAsync(string? inputFilesPath, string? outputPath, CancellationToken cancellationToken = default)
    {
        var items = this.inputReader.ReadInputs(inputFilesPath);

        if (items.Count == 0)
        {
            this.logger.LogWarning("No inputs were supplied; an empty spreadsheet will be produced.");
        }

        // Phase 0 — bounded query for the file-ID inputs and build the per-input work items
        var workItems = await this.BuildWorkItemsAsync(items, cancellationToken).ConfigureAwait(false);

        this.workspaceManager.PrepareWorkspaceRoot();

        try
        {
            // Phase 1 — parallel CaseWare reads (no database, bounded by MaxDegreeOfParallelism)
            await this.ReadCaseWareIdentifiersAsync(workItems, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.workspaceManager.CleanUpWorkspaceRoot();
        }

        // Phase 2 — one bounded staff query against the identifiers actually seen across Phase 1
        var staffByIdentifier = await this.LoadStaffAsync(workItems, cancellationToken).ConfigureAwait(false);

        // Phase 3 — in-memory mapping; no IO so this stays sequential
        var results = workItems.Select(workItem => this.BuildResult(workItem, staffByIdentifier)).ToArray();

        var resolvedOutputPath = this.ResolveOutputPath(outputPath);

        this.spreadsheetWriter.Write(results, resolvedOutputPath);

        this.logger.LogInformation("Processed {FileCount} file(s); spreadsheet written to {OutputPath}.", results.Length, resolvedOutputPath);
    }

    private async Task<List<FileWorkItem>> BuildWorkItemsAsync(
        IReadOnlyList<FileInputItem> items,
        CancellationToken cancellationToken)
    {
        var fileIds = items.Where(o => o.IsFileId).Select(o => o.FileId!.Value).Distinct().ToList();

        IReadOnlyDictionary<int, string> uncPathsById;

        // single short-lived scope just for the up-front file-ID lookup
        using (var scope = this.serviceScopeFactory.CreateScope())
        {
            var filesRepository = scope.ServiceProvider.GetRequiredService<ICaseWareFilesForApplicationsRepository>();
            uncPathsById = await filesRepository.GetUncPathsByIdsAsync(fileIds, cancellationToken).ConfigureAwait(false);
        }

        if (fileIds.Count > 0)
        {
            this.logger.LogInformation(
                "Resolved {ResolvedFileIdCount} of {RequestedFileIdCount} file ID(s) to UNC paths.",
                uncPathsById.Count,
                fileIds.Count);
        }

        var workItems = new List<FileWorkItem>(items.Count);

        foreach (var item in items)
        {
            var workItem = new FileWorkItem(item);

            if (item.IsFileId)
            {
                if (uncPathsById.TryGetValue(item.FileId!.Value, out var uncPath) && !string.IsNullOrWhiteSpace(uncPath))
                {
                    workItem.UncPath = uncPath;
                    workItem.DisplayName = $"{uncPath} (ID {item.FileId})";
                }
                else
                {
                    workItem.DisplayName = $"File ID {item.FileId}";
                    workItem.Errors.Add($"No known file was found in the database for ID {item.FileId}.");
                }
            }
            else
            {
                workItem.UncPath = item.UncPath;
                workItem.DisplayName = item.UncPath!;
            }

            workItems.Add(workItem);
        }

        return workItems;
    }

    private async Task ReadCaseWareIdentifiersAsync(IReadOnlyList<FileWorkItem> workItems, CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = this.GetMaxDegreeOfParallelism(),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            workItems,
            parallelOptions,
            (workItem, token) =>
            {
                token.ThrowIfCancellationRequested();
                this.ReadCaseWareIdentifiers(workItem);

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
    }

    private void ReadCaseWareIdentifiers(FileWorkItem workItem)
    {
        if (workItem.UncPath == null)
        {
            // the file-ID lookup already failed for this item; nothing to read from CaseWare
            return;
        }

        // each file runs in its own scope so its CaseWare session is isolated from other workers
        using var scope = this.serviceScopeFactory.CreateScope();
        var retriever = scope.ServiceProvider.GetRequiredService<ICaseWareFileUserRetriever>();

        string? workspaceDirectory = null;

        try
        {
            workspaceDirectory = this.workspaceManager.CreateWorkspace();
            var localFilePath = this.workspaceManager.CopyFileToWorkspace(workItem.UncPath, workspaceDirectory);

            var userIdentifiers = retriever.GetFileSecurityGroupUserIdentifiers(localFilePath);

            workItem.UserIdentifiers = userIdentifiers.ToList();

            if (workItem.UserIdentifiers.Count == 0)
            {
                this.logger.LogInformation(
                    "No users found in the {GroupName} security group for {File}.",
                    Constants.FileSecurityGroupName,
                    workItem.DisplayName);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "An error occurred processing file {File}.", workItem.DisplayName);
            workItem.Errors.Add($"Error processing file: {ex.Message}");
        }
        finally
        {
            if (workspaceDirectory != null)
            {
                this.workspaceManager.DeleteWorkspace(workspaceDirectory);
            }
        }
    }

    private async Task<ILookup<string, Staff>> LoadStaffAsync(
        IReadOnlyList<FileWorkItem> workItems,
        CancellationToken cancellationToken)
    {
        var uniqueIdentifiers = workItems
            .Where(workItem => workItem.UserIdentifiers != null)
            .SelectMany(workItem => workItem.UserIdentifiers!)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .ToList();

        if (uniqueIdentifiers.Count == 0)
        {
            this.logger.LogInformation(
                "No CaseWare users were found across the run; the staff database will not be queried.");

            return Enumerable.Empty<Staff>()
                             .ToLookup(staff => staff.CaseWareUserIdentifier, StringComparer.InvariantCultureIgnoreCase);
        }

        // single short-lived scope just for the bounded staff query
        using var scope = this.serviceScopeFactory.CreateScope();
        var staffRepository = scope.ServiceProvider.GetRequiredService<IStaffRepository>();

        var staff = await staffRepository.GetStaffByIdentifiersAsync(uniqueIdentifiers, cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation(
            "Loaded {StaffCount} staff record(s) for {IdentifierCount} unique CaseWare user identifier(s).",
            staff.Count,
            uniqueIdentifiers.Count);

        return staff.ToLookup(s => s.CaseWareUserIdentifier, StringComparer.InvariantCultureIgnoreCase);
    }

    private FileUserResult BuildResult(FileWorkItem workItem, ILookup<string, Staff> staffByIdentifier)
    {
        var result = new FileUserResult(workItem.DisplayName, workItem.Input.FileId, workItem.UncPath);

        foreach (var error in workItem.Errors)
        {
            result.AddError(error);
        }

        if (workItem.UserIdentifiers == null || workItem.UserIdentifiers.Count == 0)
        {
            return result;
        }

        var mappedStaff = new List<Staff>();

        foreach (var identifier in workItem.UserIdentifiers)
        {
            var matches = staffByIdentifier[identifier].ToList();

            if (matches.Count == 0)
            {
                this.logger.LogWarning("CaseWare user '{Identifier}' could not be mapped to a staff record.", identifier);
                result.AddError($"CaseWare user '{identifier}' could not be mapped to a staff record.");

                continue;
            }

            mappedStaff.AddRange(matches);
        }

        var reportableStaff = this.supportUserFilter.RemoveSupportUsers(mappedStaff);

        foreach (var staff in reportableStaff.OrderBy(o => o.FullName))
        {
            result.Users.Add(new ReportedUser(staff.FullName, staff.Office));
        }

        return result;
    }

    private int GetMaxDegreeOfParallelism()
    {
        var configured = this.options.Processing.MaxDegreeOfParallelism;

        return configured > 0 ? configured : Environment.ProcessorCount;
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

    // the per-input state that flows through the three phases:
    //   Input             - the original parsed input
    //   UncPath           - the resolved UNC path, or null if it could not be resolved
    //   DisplayName       - the human-readable label used in logging and the spreadsheet
    //   UserIdentifiers   - the FILE-group identifiers read in Phase 1; null means the file was
    //                       never opened (Phase 1 was skipped because the file-ID lookup failed,
    //                       or it threw before any identifiers were read)
    //   Errors            - file-level errors accumulated across the phases
    private sealed class FileWorkItem
    {
        public FileWorkItem(FileInputItem input)
        {
            this.Input = input;
        }

        public FileInputItem Input { get; }

        public string? UncPath { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public IReadOnlyList<string>? UserIdentifiers { get; set; }

        public List<string> Errors { get; } = new();
    }
}
