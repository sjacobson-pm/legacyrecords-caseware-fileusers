using System;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace LegacyRecordsCaseWareFileUsers.Logging;

/// <summary>
///     Wraps an <see cref="ITextFormatter" /> and prepends/appends a blank line to any event whose
///     level is at or above a configured threshold (default: <see cref="LogEventLevel.Warning" />).
///     Applied to the file, console, and debug sinks so warnings, errors, and fatals stand out
///     visually in dense logs. Not applied to Application Insights, where blank lines have no
///     structured meaning.
/// </summary>
public sealed class LevelSeparatedFormatter : ITextFormatter
{
    private readonly ITextFormatter inner;
    private readonly LogEventLevel separateAtOrAbove;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LevelSeparatedFormatter" /> class.
    /// </summary>
    /// <param name="inner">The underlying formatter that produces the event's text.</param>
    /// <param name="separateAtOrAbove">
    ///     The minimum level (inclusive) at which an event is surrounded by blank lines. Defaults to
    ///     <see cref="LogEventLevel.Warning" />.
    /// </param>
    public LevelSeparatedFormatter(ITextFormatter inner, LogEventLevel separateAtOrAbove = LogEventLevel.Warning)
    {
        ArgumentNullException.ThrowIfNull(inner);

        this.inner = inner;
        this.separateAtOrAbove = separateAtOrAbove;
    }

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var separate = logEvent.Level >= this.separateAtOrAbove;

        if (separate)
        {
            output.WriteLine();
        }

        this.inner.Format(logEvent, output);

        if (separate)
        {
            output.WriteLine();
        }
    }
}
