using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     Options controlling how a batch of files is processed. Phase 1 is a two-stage producer/consumer
///     pipeline: a copy stage pulls each <c>.ac_</c> file from its UNC share into a per-file local
///     workspace, and a CaseWare stage then opens the local copy and reads its FILE-group members.
///     The two stages have independent concurrency knobs so a network-bound copy queue does not idle
///     the CaseWare workers (and vice versa).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ProcessingOptions
{
    // The maximum number of files the CaseWare stage processes concurrently (open → read FILE group →
    // close). A value of zero or less uses the processor count. Because the CaseWare COM class is
    // registered with ThreadingModel=Apartment, concurrent sessions rely on COM's per-caller STA
    // hosting; keep this value modest and lower it to 1 if a particular host's CaseWare install
    // misbehaves with concurrent sessions.
    public int MaxDegreeOfParallelism { get; set; }

    // The maximum number of files the copy stage prefetches concurrently from the UNC share into the
    // local workspace. A value of zero or less uses MaxDegreeOfParallelism (i.e., the copy stage
    // matches the CaseWare stage's concurrency by default). Raising this above MaxDegreeOfParallelism
    // is useful when the CaseWare stage is the bottleneck and there is spare network bandwidth to
    // prefetch the next file(s) while the current one is being processed. Copies queue into a
    // bounded channel so raising this does not create unbounded local-disk pressure.
    public int MaxCopyDegreeOfParallelism { get; set; }

    // Controls what happens on --resume when a journal entry has one or more recorded errors from
    // the prior run. When false (the default), the resume "trusts the journal" — the errored entry
    // is honored verbatim and the file is not reprocessed. When true, journal entries with errors
    // are ignored on resume and the file goes through Phase 1 again as if it had never been seen;
    // the retry's outcome (success or failure) is appended to the journal alongside the prior
    // errored entry, and last-wins deduplication ensures the retry replaces the earlier record on
    // subsequent resumes.
    public bool RetryErroredFilesOnResume { get; set; }
}
