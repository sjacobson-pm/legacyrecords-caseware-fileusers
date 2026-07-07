using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Serilog;
using Serilog.Context;

namespace LegacyRecordsCaseWareFileUsers.Logging;

/// <summary>
///     Brackets a phase of processing with matched "Starting" / "Completed" (or "Failed") log lines
///     and applies a scoped indent to every log message emitted from any collaborator while the phase
///     is active. Phase boundaries appear unindented; body messages appear indented, so a reader can
///     visually group the log by phase at a glance.
///     <para>
///         The indentation is applied via a <see cref="LogContext" /> property named
///         <see cref="Logging.LogContextProperties.PhaseIndent" /> that the output template renders at
///         the start of every message. Phases do not nest — there is one flat level of indent inside
///         a phase, regardless of internal structure. Phase begin/end lines themselves are always
///         unindented because they are emitted outside the property scope.
///     </para>
/// </summary>
public static class PhaseScope
{
    /// <summary>
    ///     The exact indent string prepended to every in-phase log message. Chosen to be visually
    ///     obvious across editors and log viewers without being noisy.
    /// </summary>
    public const string PhaseIndentString = "    ";

    // A dedicated logger context so debug-template lines emitted by phase machinery are labeled
    // "Phase" instead of the calling type — matches the "phase machinery, not domain code" role.
    private static readonly ILogger PhaseLogger = Log.ForContext("SourceContext", "Phase");

    /// <summary>
    ///     Runs an asynchronous action inside a bracketed phase. Emits "Starting {phaseName}" before
    ///     the action runs, indents every log message the action produces, and emits either
    ///     "Completed {phaseName} in {elapsed}" or "Failed {phaseName} after {elapsed}" once it
    ///     finishes. Exceptions propagate; the "Failed" line is emitted just before the exception
    ///     leaves the phase.
    /// </summary>
    /// <param name="phaseName">Human-readable phase label written into the log.</param>
    /// <param name="action">The phase body.</param>
    /// <param name="showElapsed">
    ///     When <c>true</c>, the closing line includes elapsed time. Set to <c>false</c> for phases
    ///     where duration is not meaningful (for example, the Exit phase).
    /// </param>
    /// <returns>A task representing the phase execution.</returns>
    public static async Task RunAsync(string phaseName, Func<Task> action, bool showElapsed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);
        ArgumentNullException.ThrowIfNull(action);

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        PhaseLogger.Information("Starting {PhaseName}", phaseName);

        try
        {
            using (LogContext.PushProperty(LogContextProperties.PhaseIndent, PhaseIndentString))
            {
                await action().ConfigureAwait(false);
            }

            succeeded = true;
        }
        finally
        {
            stopwatch.Stop();
            EmitClosingLine(phaseName, stopwatch.Elapsed, succeeded, showElapsed);
        }
    }

    /// <summary>
    ///     Runs an asynchronous, value-returning action inside a bracketed phase. Behaves like
    ///     <see cref="RunAsync(string, Func{Task}, bool)" /> and returns the action's result.
    /// </summary>
    /// <typeparam name="T">The action's return type.</typeparam>
    /// <param name="phaseName">Human-readable phase label written into the log.</param>
    /// <param name="action">The phase body.</param>
    /// <param name="showElapsed">
    ///     When <c>true</c>, the closing line includes elapsed time.
    /// </param>
    /// <returns>The value produced by the action.</returns>
    public static async Task<T> RunAsync<T>(string phaseName, Func<Task<T>> action, bool showElapsed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);
        ArgumentNullException.ThrowIfNull(action);

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        PhaseLogger.Information("Starting {PhaseName}", phaseName);

        try
        {
            T result;

            using (LogContext.PushProperty(LogContextProperties.PhaseIndent, PhaseIndentString))
            {
                result = await action().ConfigureAwait(false);
            }

            succeeded = true;

            return result;
        }
        finally
        {
            stopwatch.Stop();
            EmitClosingLine(phaseName, stopwatch.Elapsed, succeeded, showElapsed);
        }
    }

    /// <summary>
    ///     Runs a synchronous action inside a bracketed phase.
    /// </summary>
    /// <param name="phaseName">Human-readable phase label written into the log.</param>
    /// <param name="action">The phase body.</param>
    /// <param name="showElapsed">
    ///     When <c>true</c>, the closing line includes elapsed time.
    /// </param>
    public static void Run(string phaseName, Action action, bool showElapsed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);
        ArgumentNullException.ThrowIfNull(action);

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        PhaseLogger.Information("Starting {PhaseName}", phaseName);

        try
        {
            using (LogContext.PushProperty(LogContextProperties.PhaseIndent, PhaseIndentString))
            {
                action();
            }

            succeeded = true;
        }
        finally
        {
            stopwatch.Stop();
            EmitClosingLine(phaseName, stopwatch.Elapsed, succeeded, showElapsed);
        }
    }

    /// <summary>
    ///     Runs a synchronous, value-returning action inside a bracketed phase and returns its result.
    /// </summary>
    /// <typeparam name="T">The action's return type.</typeparam>
    /// <param name="phaseName">Human-readable phase label written into the log.</param>
    /// <param name="action">The phase body.</param>
    /// <param name="showElapsed">
    ///     When <c>true</c>, the closing line includes elapsed time.
    /// </param>
    /// <returns>The value produced by the action.</returns>
    public static T Run<T>(string phaseName, Func<T> action, bool showElapsed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);
        ArgumentNullException.ThrowIfNull(action);

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        PhaseLogger.Information("Starting {PhaseName}", phaseName);

        try
        {
            T result;

            using (LogContext.PushProperty(LogContextProperties.PhaseIndent, PhaseIndentString))
            {
                result = action();
            }

            succeeded = true;

            return result;
        }
        finally
        {
            stopwatch.Stop();
            EmitClosingLine(phaseName, stopwatch.Elapsed, succeeded, showElapsed);
        }
    }

    private static void EmitClosingLine(string phaseName, TimeSpan elapsed, bool succeeded, bool showElapsed)
    {
        // Successful phases close at Information; failed phases close at Warning so they trigger the
        // blank-line separator that the file/console/debug formatter applies to Warning+ events. The
        // exception itself is logged separately by whatever catches it — the "Failed" line is just
        // the visual bracket that pairs with the "Starting" line.
        if (succeeded)
        {
            if (showElapsed)
            {
                PhaseLogger.Information(
                    "Completed {PhaseName} in {Elapsed:l}",
                    phaseName,
                    FormatElapsed(elapsed));
            }
            else
            {
                PhaseLogger.Information("Completed {PhaseName}", phaseName);
            }
        }
        else
        {
            if (showElapsed)
            {
                PhaseLogger.Warning(
                    "Failed {PhaseName} after {Elapsed:l}",
                    phaseName,
                    FormatElapsed(elapsed));
            }
            else
            {
                PhaseLogger.Warning("Failed {PhaseName}", phaseName);
            }
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        // Choose the coarsest unit that keeps the number readable at a glance: milliseconds for
        // sub-second phases, seconds for sub-minute, minutes-and-seconds for sub-hour, and
        // hours-minutes-seconds beyond that.
        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:F0}ms";
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds:F1}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
    }
}
