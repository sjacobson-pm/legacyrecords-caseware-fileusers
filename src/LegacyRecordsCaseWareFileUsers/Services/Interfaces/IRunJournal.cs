using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegacyRecordsCaseWareFileUsers.Models;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Durable, append-only record of per-file Phase 1 outcomes. One <see cref="IRunJournal" /> per
///     run — the journal file lives next to the spreadsheet output and shares the RunID stem
///     (<c>FileUsers-{RunID}.journal.jsonl</c>), so a crash mid-run leaves the journal on disk for
///     a subsequent <c>--resume</c> invocation to pick up.
/// </summary>
public interface IRunJournal
{
    /// <summary>
    ///     Gets the full path of the journal file (in-flight form, before completion).
    /// </summary>
    /// <value>The journal file path.</value>
    string JournalPath { get; }

    /// <summary>
    ///     Gets the full path of the completed-journal file (post-<see cref="MarkCompletedAsync" />).
    /// </summary>
    /// <value>The completed-journal file path.</value>
    string CompletedJournalPath { get; }

    /// <summary>
    ///     Reads every entry from the journal file, honoring last-wins deduplication by
    ///     <see cref="JournalEntry.NormalizedKey" />. A partial trailing line (typical crash
    ///     artifact) is logged and skipped so the earlier well-formed entries are still usable.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>Entries keyed by <see cref="JournalEntry.NormalizedKey" />.</returns>
    Task<IReadOnlyDictionary<string, JournalEntry>> LoadForResumeAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Appends one entry to the journal file as a single JSON line and flushes to disk. Safe to
    ///     call concurrently from multiple Phase 1 workers.
    /// </summary>
    /// <param name="entry">The entry to durably record.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>A task that completes when the entry is written and flushed.</returns>
    Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken);

    /// <summary>
    ///     Renames the in-flight journal file to the completed form. Called once, after Phase 3
    ///     successfully writes the spreadsheet, so a completed run's journal cannot be accidentally
    ///     re-resumed against.
    /// </summary>
    /// <returns>A task that completes when the rename is done.</returns>
    Task MarkCompletedAsync();
}
