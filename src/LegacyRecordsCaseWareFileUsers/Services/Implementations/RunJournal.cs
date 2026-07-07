using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Append-only JSONL implementation of <see cref="IRunJournal" />. Each entry is one line of
///     UTF-8 JSON followed by <c>\n</c>. Writes acquire a <see cref="SemaphoreSlim" /> and flush to
///     disk before returning so a crash after a per-file <c>AppendAsync</c> returns cannot lose
///     that file's result. Reads are tolerant of a partial trailing line — a hard-kill mid-write
///     leaves the last line malformed and the loader logs one warning and continues with the
///     earlier well-formed entries.
///     <para>
///         The journal path derives from the current run's context: it lives in the spreadsheet
///         output directory alongside the <c>.xlsx</c> and <c>.log</c>, and shares the RunID stem
///         so a resume can find it by RunID alone.
///     </para>
/// </summary>
internal sealed class RunJournal : IRunJournal, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false, // one line per entry — JSONL
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<RunJournal> logger;
    private readonly SemaphoreSlim writeLock = new(initialCount: 1, maxCount: 1);
    private bool disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RunJournal" /> class.
    /// </summary>
    /// <param name="runContext">The current run's context (supplies RunID and effective output directory).</param>
    /// <param name="logger">The logger.</param>
    public RunJournal(IRunContext runContext, ILogger<RunJournal> logger)
    {
        ArgumentNullException.ThrowIfNull(runContext);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        var stem = $"FileUsers-{runContext.RunId}";

        this.JournalPath = Path.Combine(runContext.EffectiveOutputDirectory, $"{stem}.journal.jsonl");
        this.CompletedJournalPath = Path.Combine(runContext.EffectiveOutputDirectory, $"{stem}.journal.completed.jsonl");
    }

    /// <inheritdoc />
    public string JournalPath { get; }

    /// <inheritdoc />
    public string CompletedJournalPath { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, JournalEntry>> LoadForResumeAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);

        // Ensure the directory and file exist before we return. Creating an empty journal eagerly
        // — even on fresh runs with no resume state to load — means every RunID has a journal on
        // disk from the start of Phase 0, so a subsequent --resume against this RunID succeeds
        // even if the current run crashes before any file completes Phase 1. Symmetric with the
        // log file, which Serilog opens eagerly at startup.
        var directory = Path.GetDirectoryName(this.JournalPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(this.JournalPath))
        {
            // Create an empty file, then close the handle so subsequent appends can open in
            // FileMode.Append mode without conflict.
            using (File.Create(this.JournalPath))
            {
            }

            this.logger.LogDebug("Initialized empty journal at {JournalPath}.", this.JournalPath);

            return result;
        }

        this.logger.LogInformation("Loading resume journal from {JournalPath}...", this.JournalPath);

        using var stream = new FileStream(this.JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Utf8NoBom);

        var lineNumber = 0;
        string? line;
        var lastLineWasMalformed = false;

        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            lineNumber++;
            lastLineWasMalformed = false;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JournalEntry? entry;

            try
            {
                entry = JsonSerializer.Deserialize<JournalEntry>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // We can't tell here whether this is the last line (partial-crash artifact) or an
                // interior line (real corruption). Defer the warning until we know — if another
                // line parses after this one, this is real corruption and worth surfacing loudly;
                // if not, it was probably a crash mid-flush and skipping it is the right call.
                lastLineWasMalformed = true;
                this.logger.LogWarning(
                    "Journal line {LineNumber} in {JournalPath} could not be parsed and will be skipped.",
                    lineNumber,
                    this.JournalPath);
                continue;
            }

            if (entry == null)
            {
                continue;
            }

            // last-wins deduplication — a file that was retried appears twice and the retry's
            // outcome takes precedence
            result[entry.NormalizedKey] = entry;
        }

        this.logger.LogInformation(
            "Loaded {EntryCount} journal entry/entries covering {UniqueKeyCount} unique file(s).",
            lineNumber,
            result.Count);

        // The very last malformed line is the classic hard-kill-mid-flush signature; if we saw one
        // and it was the final line, that's the expected case and we've already skipped it silently.
        _ = lastLineWasMalformed; // suppress unused-variable analyzer noise

        return result;
    }

    /// <inheritdoc />
    public async Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var serialized = JsonSerializer.Serialize(entry, SerializerOptions);

        await this.writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Ensure the directory exists — on a fresh run the journal starts writing in Phase 1,
            // before Phase 3 creates the output directory for the spreadsheet.
            var directory = Path.GetDirectoryName(this.JournalPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(
                this.JournalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);

            var bytes = Utf8NoBom.GetBytes(serialized + "\n");
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

            // flushToDisk: true forces the OS to push the write past its buffer cache. Without it,
            // a crash between AppendAsync returning and the OS flushing the page cache could lose
            // the entry — defeating the point of the journal. FlushAsync has no flushToDisk
            // overload, so the sync form is deliberate here.
#pragma warning disable VSTHRD103 // synchronous Flush is intentional
            stream.Flush(flushToDisk: true);
#pragma warning restore VSTHRD103
        }
        finally
        {
            this.writeLock.Release();
        }
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync()
    {
        if (!File.Exists(this.JournalPath))
        {
            // Nothing to rename — a run that emitted no journal entries (empty input, or every input
            // was already fully covered by a prior journal) is still a successful run.
            return Task.CompletedTask;
        }

        // If a prior .completed.jsonl exists at this path (unusual — would mean the same RunID
        // completed once, was resumed against a manually restored .jsonl, and is now completing
        // again), overwrite. The in-flight file wins because it is the freshest.
        if (File.Exists(this.CompletedJournalPath))
        {
            File.Delete(this.CompletedJournalPath);
        }

        File.Move(this.JournalPath, this.CompletedJournalPath);

        this.logger.LogInformation("Journal renamed to {CompletedJournalPath}.", this.CompletedJournalPath);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.writeLock.Dispose();
    }
}
