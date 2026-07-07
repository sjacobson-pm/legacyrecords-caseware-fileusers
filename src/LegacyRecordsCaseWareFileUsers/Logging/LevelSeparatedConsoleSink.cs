using System;
using System.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LegacyRecordsCaseWareFileUsers.Logging;

/// <summary>
///     Console sink that surrounds Warning+ events with a blank line while preserving Serilog's
///     built-in console theme (level-based colors). Uses an internal sub-logger to delegate the
///     actual event rendering to <c>Serilog.Sinks.Console</c>, which applies its default theme;
///     the blank lines are written directly to <see cref="Console.Out" /> before and after, without
///     going through the sub-logger's formatter.
///     <para>
///         The alternative pattern — wrapping the console sink's <see cref="Serilog.Formatting.ITextFormatter" /> —
///         loses the console theme because the theme is applied by the sink, not the formatter. This
///         wrapper keeps both concerns intact.
///     </para>
/// </summary>
public sealed class LevelSeparatedConsoleSink : ILogEventSink, IDisposable
{
    private readonly Logger inner;
    private readonly LogEventLevel separateAtOrAbove;
    private readonly Lock emitLock = new();
    private bool disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LevelSeparatedConsoleSink" /> class.
    /// </summary>
    /// <param name="outputTemplate">The output template handed to Serilog's console sink.</param>
    /// <param name="separateAtOrAbove">
    ///     The minimum level (inclusive) at which an event is surrounded by blank lines. Defaults to
    ///     <see cref="LogEventLevel.Warning" />.
    /// </param>
    public LevelSeparatedConsoleSink(string outputTemplate, LogEventLevel separateAtOrAbove = LogEventLevel.Warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputTemplate);

        // The sub-logger accepts events at every level — the outer logger has already applied the
        // configured level filter by the time this sink's Emit is invoked, so double-filtering here
        // would just drop events unexpectedly.
        this.inner = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(outputTemplate: outputTemplate)
            .CreateLogger();

        this.separateAtOrAbove = separateAtOrAbove;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var separate = logEvent.Level >= this.separateAtOrAbove;

        // Serilog's console sink synchronizes its own output internally, but the "blank before →
        // event → blank after" triple must be atomic to avoid interleaving with a concurrent Emit
        // — two Warning events on different threads would otherwise produce (blank, blank, ev1,
        // ev2, blank, blank) instead of (blank, ev1, blank, blank, ev2, blank).
        lock (this.emitLock)
        {
            if (separate)
            {
                Console.WriteLine();
            }

            // Write(LogEvent) delivers the fully-enriched event to the sub-logger's sinks without
            // re-processing enrichers, so nothing added by the outer logger is lost.
            this.inner.Write(logEvent);

            if (separate)
            {
                Console.WriteLine();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.inner.Dispose();
    }
}
