using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Data.Domain;
using LegacyRecordsCaseWareFileUsers.Data.Repositories;
using LegacyRecordsCaseWareFileUsers.Helpers;
using LegacyRecordsCaseWareFileUsers.Logging;
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
    private readonly IRunContext runContext;
    private readonly IRunJournal runJournal;

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
    /// <param name="runContext">The current run's context (supplies the RunID used to name the
    /// default output file when neither <c>--output</c> nor <c>Output:FilePath</c> is set).</param>
    /// <param name="runJournal">The durable per-file record consulted for resume and appended to
    /// as Phase 1 completes each file.</param>
    public FileUserService(
        IOptions<ConfigurationOptions> optionsAccessor,
        ILogger<FileUserService> logger,
        IInputReader inputReader,
        IWorkspaceManager workspaceManager,
        IFileUserSpreadsheetWriter spreadsheetWriter,
        IServiceScopeFactory serviceScopeFactory,
        ISupportUserFilter supportUserFilter,
        IRunContext runContext,
        IRunJournal runJournal)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(inputReader);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(spreadsheetWriter);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(supportUserFilter);
        ArgumentNullException.ThrowIfNull(runContext);
        ArgumentNullException.ThrowIfNull(runJournal);

        this.options = optionsAccessor.Value;
        this.logger = logger;
        this.inputReader = inputReader;
        this.workspaceManager = workspaceManager;
        this.spreadsheetWriter = spreadsheetWriter;
        this.serviceScopeFactory = serviceScopeFactory;
        this.supportUserFilter = supportUserFilter;
        this.runContext = runContext;
        this.runJournal = runJournal;
    }

    /// <inheritdoc />
    public async Task RunAsync(string? inputFilesPath, string? outputPath, CancellationToken cancellationToken = default)
    {
        // Input parsing — read and classify the input list (file IDs vs. UNC paths)
        var items = PhaseScope.Run(
            "Input parsing",
            () =>
            {
                var parsed = this.inputReader.ReadInputs(inputFilesPath);

                if (parsed.Count == 0)
                {
                    this.logger.LogWarning("No inputs were supplied; an empty spreadsheet will be produced.");
                }

                return parsed;
            });

        // Phase 0 — bounded query for the file-ID inputs and build the per-input work items, then
        // consult the resume journal to skip any that were already processed in a prior run
        var workItems = await PhaseScope.RunAsync(
            "Phase 0 — Resolve file IDs",
            async () =>
            {
                var built = await this.BuildWorkItemsAsync(items, cancellationToken).ConfigureAwait(false);
                await this.ApplyResumeJournalAsync(built, cancellationToken).ConfigureAwait(false);

                return built;
            }).ConfigureAwait(false);

        // Phase 1 — parallel CaseWare reads (no database, bounded by MaxDegreeOfParallelism)
        await PhaseScope.RunAsync(
            "Phase 1 — CaseWare identifier reads",
            async () =>
            {
                this.workspaceManager.PrepareWorkspaceRoot();

                try
                {
                    await this.ReadCaseWareIdentifiersAsync(workItems, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    this.workspaceManager.CleanUpWorkspaceRoot();
                }
            }).ConfigureAwait(false);

        // Phase 2 — one bounded staff query against the identifiers actually seen across Phase 1
        var staffByIdentifier = await PhaseScope.RunAsync(
            "Phase 2 — Resolve staff",
            () => this.LoadStaffAsync(workItems, cancellationToken)).ConfigureAwait(false);

        // Phase 3 — in-memory mapping (no IO so this stays sequential) + spreadsheet emit
        await PhaseScope.RunAsync(
            "Phase 3 — Map, filter, and emit",
            () =>
            {
                var results = workItems.Select(workItem => this.BuildResult(workItem, staffByIdentifier)).ToArray();

                var resolvedOutputPath = this.ResolveOutputPath(outputPath);

                this.spreadsheetWriter.Write(results, resolvedOutputPath);

                this.logger.LogInformation("Processed {FileCount} file(s); spreadsheet written to {OutputPath}.", results.Length, resolvedOutputPath);

                return Task.CompletedTask;
            }).ConfigureAwait(false);

        // Rename the in-flight journal so a subsequent --resume against this RunID fails loudly.
        // Fires only after Phase 3 succeeds; if Phase 3 throws, the .journal.jsonl is left as-is
        // for a resume attempt to pick up.
        await this.runJournal.MarkCompletedAsync().ConfigureAwait(false);
    }

    private async Task<List<FileWorkItem>> BuildWorkItemsAsync(IReadOnlyList<FileInputItem> items, CancellationToken cancellationToken)
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
                    workItem.NormalizedKey = this.NormalizeUncPath(uncPath);
                    workItem.DisplayName = $"{uncPath} (ID {item.FileId})";
                    workItem.LogName = $"{LogPathFormatter.FormatForLog(uncPath)} (ID {item.FileId})";
                }
                else
                {
                    workItem.DisplayName = $"File ID {item.FileId}";
                    workItem.LogName = $"File ID {item.FileId}";
                    workItem.Errors.Add($"No known file was found in the database for ID {item.FileId}.");
                }
            }
            else
            {
                workItem.UncPath = item.UncPath;
                workItem.NormalizedKey = this.NormalizeUncPath(item.UncPath!);
                workItem.DisplayName = item.UncPath!;
                workItem.LogName = LogPathFormatter.FormatForLog(item.UncPath);
            }

            workItems.Add(workItem);
        }

        return workItems;
    }

    /// <summary>
    ///     Normalizes a UNC path to the identity key used for resume matching. Uses
    ///     <see cref="Path.GetFullPath(string)" /> to canonicalize any relative components, then
    ///     lowercases via <see cref="string.ToLowerInvariant" /> so case-insensitive UNC paths
    ///     (<c>\\SRV\a.ac_</c> vs. <c>\\srv\a.ac_</c>) compare equal.
    /// </summary>
    /// <param name="uncPath">The UNC path to normalize.</param>
    /// <returns>The normalized identity key.</returns>
    private string NormalizeUncPath(string uncPath)
    {
        _ = this; // instance method to satisfy SA1204 (statics before instance) without moving the helper away from its only caller
        return Path.GetFullPath(uncPath).ToLowerInvariant();
    }

    /// <summary>
    ///     If a resume journal is present, applies its entries to the corresponding work items:
    ///     pre-populates <c>UserIdentifiers</c> and <c>Errors</c> from the journal entry, and marks
    ///     the item as pre-populated so Phase 1 skips it. Entries with recorded errors are honored
    ///     only when <c>Processing:RetryErroredFilesOnResume</c> is <c>false</c>; when <c>true</c>,
    ///     they are ignored so the file is reprocessed and a fresh journal entry appended.
    /// </summary>
    /// <param name="workItems">The work items built by <see cref="BuildWorkItemsAsync" />.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>A task that completes when the resume state has been applied.</returns>
    private async Task ApplyResumeJournalAsync(IReadOnlyList<FileWorkItem> workItems, CancellationToken cancellationToken)
    {
        var journalDict = await this.runJournal.LoadForResumeAsync(cancellationToken).ConfigureAwait(false);

        if (journalDict.Count == 0)
        {
            return;
        }

        var retryErroredFiles = this.options.Processing.RetryErroredFilesOnResume;
        var skippedCount = 0;
        var retriedCount = 0;

        foreach (var workItem in workItems)
        {
            if (workItem.NormalizedKey == null)
            {
                continue;
            }

            if (!journalDict.TryGetValue(workItem.NormalizedKey, out var entry))
            {
                continue;
            }

            if (entry.Errors.Count > 0 && retryErroredFiles)
            {
                // journal entry has errors and the caller opted into retrying — leave the work
                // item untouched so it flows through Phase 1 as a fresh read
                retriedCount++;
                continue;
            }

            // pre-populate identifiers and errors from the journal entry; Phase 1 will skip it
            workItem.UserIdentifiers = entry.UserIdentifiers.ToList();
            workItem.Errors.AddRange(entry.Errors);
            workItem.PrePopulatedFromJournal = true;
            skippedCount++;
        }

        this.logger.LogInformation(
            "Resume: {SkippedCount} file(s) skipped from prior journal; {RetriedCount} previously-errored file(s) will be retried (RetryErroredFilesOnResume = {RetryFlag}).",
            skippedCount,
            retriedCount,
            retryErroredFiles);
    }

    private async Task ReadCaseWareIdentifiersAsync(IReadOnlyList<FileWorkItem> workItems, CancellationToken cancellationToken)
    {
        // Items whose Phase 0 file-ID lookup failed have no UNC path; items that were pre-populated
        // from a resume journal entry already have their identifiers/errors from the prior run.
        // Both categories skip the Phase 1 pipeline entirely — no copy, no CaseWare open, no
        // workspace churn.
        var itemsToProcess = workItems.Where(item => item.UncPath != null && !item.PrePopulatedFromJournal).ToList();

        if (itemsToProcess.Count == 0)
        {
            return;
        }

        var caseWareDop = this.GetMaxDegreeOfParallelism();
        var copyDop = this.GetCopyMaxDegreeOfParallelism();

        // The channel is the seam between the copy stage (producer) and the CaseWare stage (consumer).
        // Bounded capacity applies backpressure so the copy stage cannot run arbitrarily far ahead of
        // the (slower) CaseWare stage and pile up on local disk. A small multiple of the CaseWare DOP
        // keeps every CaseWare worker fed with at most one queued file and one in-flight file — enough
        // to hide the copy latency behind CaseWare work without unbounded prefetch.
        var channelCapacity = Math.Max(caseWareDop * 2, 1);

        var channel = Channel.CreateBounded<StagedFile>(
            new BoundedChannelOptions(channelCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false, });

        this.logger.LogInformation(
            "Phase 1 pipeline: copy DOP {CopyDop}, CaseWare DOP {CaseWareDop}, channel capacity {Capacity}, {ItemCount} file(s) to process.",
            copyDop,
            caseWareDop,
            channelCapacity,
            itemsToProcess.Count);

        // start the consumer first so the channel drains as producers push. WriteAsync applies
        // backpressure automatically when the channel is full, so starting the producer next is safe.
        var consumerTask = this.ConsumeStagedFilesAsync(channel.Reader, caseWareDop, cancellationToken);

        try
        {
            await this.ProduceStagedFilesAsync(itemsToProcess, channel.Writer, copyDop, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // signal the consumer to drain and exit — must happen even if the producer threw or was
            // cancelled, otherwise the consumer would wait forever on an empty channel
            channel.Writer.TryComplete();
        }

        await consumerTask.ConfigureAwait(false);
    }

    private async Task ProduceStagedFilesAsync(
        IReadOnlyList<FileWorkItem> itemsToProcess,
        ChannelWriter<StagedFile> writer,
        int copyDop,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = copyDop, CancellationToken = cancellationToken, };

        await Parallel.ForEachAsync(
                           itemsToProcess,
                           parallelOptions,
                           async (workItem, token) => await this.StageFileAsync(workItem, writer, token).ConfigureAwait(false))
                      .ConfigureAwait(false);
    }

    private async Task StageFileAsync(FileWorkItem workItem, ChannelWriter<StagedFile> writer, CancellationToken cancellationToken)
    {
        string? workspaceDirectory = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            workspaceDirectory = this.workspaceManager.CreateWorkspace();
            var localFilePath = this.workspaceManager.CopyFileToWorkspace(workItem.UncPath!, workspaceDirectory);

            // hand ownership of the workspace to the consumer via the channel; from here on the
            // CaseWare stage is responsible for deleting it after processing
            await writer.WriteAsync(new StagedFile(workItem, workspaceDirectory, localFilePath), cancellationToken).ConfigureAwait(false);

            // ownership transferred — do not delete in the finally below
            workspaceDirectory = null;
        }
        catch (OperationCanceledException)
        {
            // on cancellation the workspace is still ours to clean up; rethrow so the parallel loop
            // observes the cancellation
            if (workspaceDirectory != null)
            {
                this.workspaceManager.DeleteWorkspace(workspaceDirectory);
            }

            throw;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "An error occurred copying file {File} to the workspace.", workItem.LogName);
            workItem.Errors.Add($"Error copying file: {ex.Message}");

            if (workspaceDirectory != null)
            {
                this.workspaceManager.DeleteWorkspace(workspaceDirectory);
            }

            // The copy failed durably enough to be worth recording — journal this outcome so a
            // resume can honor the "file was tried and failed" record (or retry it, per the
            // RetryErroredFilesOnResume setting).
            await this.AppendJournalEntryAsync(workItem).ConfigureAwait(false);
        }
    }

    private async Task ConsumeStagedFilesAsync(ChannelReader<StagedFile> reader, int caseWareDop, CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = caseWareDop, CancellationToken = cancellationToken, };

        await Parallel.ForEachAsync(
                           reader.ReadAllAsync(cancellationToken),
                           parallelOptions,
                           async (stagedFile, token) =>
                           {
                               token.ThrowIfCancellationRequested();
                               await this.ProcessStagedFileAsync(stagedFile).ConfigureAwait(false);
                           })
                      .ConfigureAwait(false);
    }

    private async Task ProcessStagedFileAsync(StagedFile stagedFile)
    {
        var workItem = stagedFile.WorkItem;

        // each file runs in its own scope so its CaseWare session is isolated from other workers
        using var scope = this.serviceScopeFactory.CreateScope();
        var retriever = scope.ServiceProvider.GetRequiredService<ICaseWareFileUserRetriever>();

        try
        {
            var userIdentifiers = retriever.GetFileSecurityGroupUserIdentifiers(stagedFile.LocalFilePath);

            workItem.UserIdentifiers = userIdentifiers.ToList();

            if (workItem.UserIdentifiers.Count == 0)
            {
                this.logger.LogInformation(
                    "No users found in the {GroupName} security group for {File}.",
                    Constants.FileSecurityGroupName,
                    workItem.LogName);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "An error occurred processing file {File}.", workItem.LogName);
            workItem.Errors.Add($"Error processing file: {ex.Message}");
        }
        finally
        {
            // Journal the per-file outcome durably BEFORE workspace cleanup so a crash mid-cleanup
            // still leaves a valid record for a subsequent --resume to honor.
            await this.AppendJournalEntryAsync(workItem).ConfigureAwait(false);
            this.workspaceManager.DeleteWorkspace(stagedFile.WorkspaceDirectory);
        }
    }

    /// <summary>
    ///     Builds a <see cref="JournalEntry" /> from a work item's current state and appends it to
    ///     the journal. Uses <see cref="CancellationToken.None" /> so a Ctrl+C after the CaseWare
    ///     read finished does not lose the record of that read.
    /// </summary>
    /// <param name="workItem">The work item whose current state should be captured.</param>
    /// <returns>A task that completes once the entry is written and flushed.</returns>
    private async Task AppendJournalEntryAsync(FileWorkItem workItem)
    {
        if (workItem.UncPath == null || workItem.NormalizedKey == null)
        {
            // Phase 0 failures never reach Phase 1 and have no UNC path — nothing to journal
            return;
        }

        var entry = new JournalEntry
        {
            Timestamp = DateTime.UtcNow,
            UncPath = workItem.UncPath,
            NormalizedKey = workItem.NormalizedKey,
            FileId = workItem.Input.FileId,
            DisplayName = workItem.DisplayName,
            UserIdentifiers = (workItem.UserIdentifiers ?? Array.Empty<string>()).ToArray(),
            Errors = workItem.Errors.ToArray(),
        };

        await this.runJournal.AppendAsync(entry, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<ILookup<string, Staff>> LoadStaffAsync(IReadOnlyList<FileWorkItem> workItems, CancellationToken cancellationToken)
    {
        var uniqueIdentifiers = workItems.Where(workItem => workItem.UserIdentifiers != null)
                                         .SelectMany(workItem => workItem.UserIdentifiers!)
                                         .Distinct(StringComparer.InvariantCultureIgnoreCase)
                                         .ToList();

        if (uniqueIdentifiers.Count == 0)
        {
            this.logger.LogInformation("No CaseWare users were found across the run; the staff database will not be queried.");

            return Enumerable.Empty<Staff>().ToLookup(staff => staff.CaseWareUserIdentifier, StringComparer.InvariantCultureIgnoreCase);
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
        var share = UncShareExtractor.Extract(workItem.UncPath, this.options.Output.ShareSegmentIndex);
        var result = new FileUserResult(workItem.DisplayName, workItem.Input.FileId, workItem.UncPath, share);

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
            result.Users.Add(new ReportedUser(staff.FullName, staff.Office, staff.Position));
        }

        return result;
    }

    private int GetMaxDegreeOfParallelism()
    {
        var configured = this.options.Processing.MaxDegreeOfParallelism;

        return configured > 0 ? configured : Environment.ProcessorCount;
    }

    private int GetCopyMaxDegreeOfParallelism()
    {
        var configured = this.options.Processing.MaxCopyDegreeOfParallelism;

        // zero or less falls through to the CaseWare DOP so the copy stage matches the CaseWare stage
        // out of the box; setting it higher lets the copy stage prefetch ahead of the CaseWare stage
        return configured > 0 ? configured : this.GetMaxDegreeOfParallelism();
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

        // Default output filename derives from the RunID so every artifact of a single run (log,
        // journal, spreadsheet) shares the same stem. Callers who want a semantic filename can
        // still override via `--output` or `Output:FilePath`.
        var fileName = $"FileUsers-{this.runContext.RunId}.xlsx";

        return Path.Combine(directory, fileName);
    }

    // A work item that has completed the copy stage of Phase 1 and is ready for the CaseWare stage.
    // The copy stage transfers ownership of the workspace directory to the consumer via this
    // record; the CaseWare stage is responsible for calling DeleteWorkspace after processing.
    private sealed record StagedFile(FileWorkItem WorkItem, string WorkspaceDirectory, string LocalFilePath);

    // the per-input state that flows through the three phases:
    //   Input                    - the original parsed input
    //   UncPath                  - the resolved UNC path, or null if it could not be resolved
    //   NormalizedKey            - lowercase-full-path identity key for resume matching; null when
    //                              UncPath is null
    //   DisplayName              - the full human-readable label written to the spreadsheet
    //   LogName                  - the compact label used in log messages (just file name and parent)
    //   UserIdentifiers          - the FILE-group identifiers read in Phase 1; null means the file
    //                              was never opened (Phase 1 was skipped because the file-ID
    //                              lookup failed, or it threw before any identifiers were read)
    //   Errors                   - file-level errors accumulated across the phases
    //   PrePopulatedFromJournal  - true when the item's identifiers/errors came from a resume
    //                              journal entry rather than a fresh Phase 1 read; such items skip
    //                              the Phase 1 pipeline entirely
    private sealed class FileWorkItem
    {
        public FileWorkItem(FileInputItem input)
        {
            this.Input = input;
        }

        public FileInputItem Input { get; }

        public string? UncPath { get; set; }

        public string? NormalizedKey { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public string LogName { get; set; } = string.Empty;

        public IReadOnlyList<string>? UserIdentifiers { get; set; }

        public List<string> Errors { get; } = new();

        public bool PrePopulatedFromJournal { get; set; }
    }
}
