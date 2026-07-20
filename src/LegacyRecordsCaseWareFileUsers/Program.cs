using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using LegacyRecordsCaseWareFileUsers.CommandLineOptions;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using LegacyRecordsCaseWareFileUsers.Helpers;
using LegacyRecordsCaseWareFileUsers.Helpers.Extensions;
using LegacyRecordsCaseWareFileUsers.Logging;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Display;
using ILogger = Serilog.ILogger;

[assembly: InternalsVisibleTo("LegacyRecordsCaseWareFileUsers.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace LegacyRecordsCaseWareFileUsers;

[ExcludeFromCodeCoverage(Justification = "The main program class is not tested.")]
internal class Program
{
    public static readonly Guid EngineInstanceId = Guid.NewGuid();

    private const int CancelledExitCode = 2;

    private static IServiceCollection services = null!;
    private static IConfigurationRoot configuration = null!;
    private static ConfigurationOptions configOptions = null!;
    private static ILogger log = null!;
    private static string runId = null!;
    private static string effectiveOutputDirectory = null!;
    private static int exitCode;

    private static async Task Main(string[] args)
    {
        exitCode = 0;

        try
        {
            BindConfigurationOptions();

            // Parse command-line arguments early — before logging is configured — so the effective
            // output directory (which may come from `--output`) is known when the file-log sink is
            // set up and when the RunID's collision check runs. The parser prints its own errors
            // and help text to Console.Error, so nothing is lost by not having Serilog yet.
            var parseResult = Parser.Default.ParseArguments<ProduceFileUserListOptions>(args);
            var parsedOptions = (parseResult as Parsed<ProduceFileUserListOptions>)?.Value;

            effectiveOutputDirectory = ResolveOutputDirectory(parsedOptions?.OutputPath);

            // Adopt the RunID from --resume when provided, otherwise generate a fresh one. The
            // resume target's existence is validated below, after logging is configured, so any
            // failure surfaces through the normal logged pathway (console color, log file, and the
            // "Press any key to exit" prompt) instead of a bare stderr dump that vanishes with
            // Environment.Exit.
            var isResuming = !string.IsNullOrWhiteSpace(parsedOptions?.ResumeRunId);
            runId = isResuming
                ? parsedOptions!.ResumeRunId!.Trim()
                : RunIdGenerator.GenerateUnique(effectiveOutputDirectory);

            ConfigureServices();
            ConfigureLogging();

            // The first line every log stream sees identifies the run — critical when troubleshooting
            // hours-long batches or resuming a crashed run against its journal.
            log.Information(
                "Starting {ApplicationTitle} — Run ID: {RunId} — arguments: {Arguments}",
                Constants.ApplicationTitle,
                runId,
                JsonConvert.SerializeObject(args));

            await TryRunParsedCommandAsync(
                isResuming,
                () => parseResult.MapResult(
                    async options => await ProduceFileUserListAsync(options),
                    async errors => await HandleCommandLineParsingErrorsAsync(args, errors)));
        }
        catch (OperationCanceledException)
        {
            // the run was cancelled (for example, the user pressed Ctrl+C); this is expected and is
            // not an error, so report it cleanly without a stack trace
            log.Warning("Processing was cancelled before it completed; no spreadsheet was produced.");

            exitCode = CancelledExitCode;
        }
        catch (OptionsValidationException ex)
        {
            // a required configuration value is missing or invalid; report it cleanly, no stack trace
            // (logging is always configured by the time options are validated)
            var failures = string.Join(Environment.NewLine, ex.Failures.Select(failure => $"  - {failure}"));
            var message = $"The application cannot start because required configuration is missing or invalid:{Environment.NewLine}{failures}";

            log.Fatal("{ConfigurationError:l}", message);

            exitCode = -1;
        }
        catch (Exception ex)
        {
            if (log == null)
            {
                throw;
            }

            log.Fatal(ex, "An unhandled exception has occurred!");

            exitCode = -1;
        }

        // Exit phase — no elapsed timer (the "how long did the run take" answer is aggregated across
        // the work phases, not the exit banner). Wrap the "Exiting..." line and the "Press any key"
        // prompt so both appear under the same visual bracket.
        PhaseScope.Run(
            "Exit",
            () =>
            {
                log.Information("Exiting {ApplicationTitle} with exit code {ExitCode}...", Constants.ApplicationTitle, exitCode);
                WaitForExitKeyPress();
            },
            showElapsed: false);

        await Log.CloseAndFlushAsync();

        Environment.Exit(exitCode);
    }

    /// <summary>
    ///     Executes the parsed command branch unless resume validation fails. Returns
    ///     <c>false</c> when resume validation blocks command execution.
    /// </summary>
    /// <param name="isResuming">Whether the run requested <c>--resume</c>.</param>
    /// <param name="runParsedCommandAsync">Delegate that executes the parser's command branch.</param>
    /// <returns><c>true</c> when command execution ran; <c>false</c> when resume validation blocked it.</returns>
    private static async Task<bool> TryRunParsedCommandAsync(bool isResuming, Func<Task> runParsedCommandAsync)
    {
        ArgumentNullException.ThrowIfNull(runParsedCommandAsync);

        if (isResuming && !ValidateResumeJournal(runId, effectiveOutputDirectory))
        {
            // ValidateResumeJournal already logged the clean failure message and set exitCode.
            // Skip command execution while still allowing the shared exit/log-flush path to run.
            return false;
        }

        await runParsedCommandAsync().ConfigureAwait(false);

        return true;
    }

    /// <summary>
    ///     Verifies that a journal file matching the supplied RunID is present and not already
    ///     marked completed. Called after logging is configured so the fail path can log via
    ///     <c>log.Fatal</c> — the message reaches the console (with color), the run's log file, and
    ///     Application Insights if configured. Sets <see cref="exitCode" /> and returns
    ///     <c>false</c> on failure so the caller can skip subsequent work while still running the
    ///     normal exit sequence (banner, log flush, keypress prompt).
    /// </summary>
    /// <param name="resumeRunId">The RunID supplied to <c>--resume</c>.</param>
    /// <param name="outputDirectory">The effective output directory to look in.</param>
    /// <returns><c>true</c> if resume can proceed; <c>false</c> if it must be aborted.</returns>
    private static bool ValidateResumeJournal(string resumeRunId, string outputDirectory)
    {
        var journalPath = Path.Combine(outputDirectory, $"FileUsers-{resumeRunId}.journal.jsonl");
        var completedJournalPath = Path.Combine(outputDirectory, $"FileUsers-{resumeRunId}.journal.completed.jsonl");

        if (File.Exists(completedJournalPath))
        {
            log.Fatal(
                "Resume was requested for Run ID '{RunId}' but the journal at '{CompletedJournalPath}' is already marked completed. " +
                "Rename it back to '.journal.jsonl' by hand if you truly want to resume against a completed journal, or omit --resume to start a fresh run.",
                resumeRunId,
                completedJournalPath);

            exitCode = -1;

            return false;
        }

        if (!File.Exists(journalPath))
        {
            log.Fatal(
                "Resume was requested for Run ID '{RunId}' but no journal was found at '{JournalPath}'. " +
                "Omit --resume to start a fresh run.",
                resumeRunId,
                journalPath);

            exitCode = -1;

            return false;
        }

        return true;
    }

    /// <summary>
    ///     Pauses before exit so output remains visible when the program is run interactively.
    ///     Emits the prompt as a normal log line so the file log captures it alongside the exit
    ///     banner; skipped when input is redirected (non-interactive / CI runs).
    /// </summary>
    private static void WaitForExitKeyPress()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        log.Information("Press any key to exit...");
        Console.ReadKey(true);
    }

    /// <summary>
    ///     Handles command line parsing errors.
    /// </summary>
    /// <param name="commandLineArgs">
    ///     The arguments that were passed at the command line.
    /// </param>
    /// <param name="errors">
    ///     The collection of errors that occurred.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    /// </returns>
    private static async Task HandleCommandLineParsingErrorsAsync(string[] commandLineArgs, IEnumerable<Error> errors)
    {
        var errorsList = errors.ToList();

        if (!errorsList.IsHelp() && !errorsList.IsVersion())
        {
            log.Error("The following command line parsing errors occurred: {@CommandLineParsingErrors}", errorsList);

            await Task.CompletedTask;

            exitCode = -1;
        }
        else
        {
            log.Information("Help was requested: {HelpArgs}", commandLineArgs);
        }
    }

    //// ****************************************************************************************
    //// application setup and configuration
    //// ****************************************************************************************

    /// <summary>
    ///     Binds all configuration options.
    /// </summary>
    private static void BindConfigurationOptions()
    {
        var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                                                .AddJsonFile("appsettings.json", false)
                                                .AddJsonFile("appsettings.local.json", true);

        configOptions = new ConfigurationOptions();

        configuration = builder.Build();
        configuration.Bind(configOptions);
    }

    /// <summary>
    ///     Configure services and dependency injection.
    /// </summary>
    private static void ConfigureServices()
    {
        services = new ServiceCollection();

        // logging
        var loggerFactory = new LoggerFactory().AddSerilog();
        services.AddSingleton(loggerFactory).AddLogging();

        // options
        services.AddOptions().Configure<ConfigurationOptions>(configuration);

        // validate required configuration sections at start-up (fail fast on misconfiguration)
        services.AddOptions<ConnectionStringOptions>()
                .Bind(configuration.GetSection(ConnectionStringOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddOptions<CaseWareOptions>()
                .Bind(configuration.GetSection(CaseWareOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        // services
        services.AddConsoleAppServices(configOptions, runId, effectiveOutputDirectory);
    }

    /// <summary>
    ///     Configure logging.
    /// </summary>
    private static void ConfigureLogging()
    {
        var loggerConfiguration = new LoggerConfiguration();

        // Debug and file sinks render through LevelSeparatedFormatter (formatter wrapping) so
        // Warning+ events are surrounded by blank lines. The console sink uses LevelSeparatedConsoleSink
        // (sink wrapping) instead — that approach preserves Serilog's built-in console theme colors,
        // which the formatter-wrapping approach would otherwise lose. Application Insights is not
        // wrapped either way — blank lines have no meaning in structured telemetry.
        var debugFormatter = new LevelSeparatedFormatter(new MessageTemplateTextFormatter(configOptions.Logging.DebugOutputTemplate));

        loggerConfiguration.MinimumLevel.ControlledBy(LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Default))
                           .MinimumLevel.Override("Microsoft", LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Microsoft))
                           .MinimumLevel.Override("System", LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.System))
                           .Enrich.FromLogContext()
                           .Enrich.WithProcessId()
                           .Enrich.WithMachineName()
                           .Enrich.WithProperty(LogContextProperties.EngineInstance, EngineInstanceId)
                           .Enrich.WithProperty(LogContextProperties.RunId, runId)
                           .WriteTo.Debug(
                                formatter: debugFormatter,
                                levelSwitch: LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Debug))
                           .WriteTo.Sink(
                                new LevelSeparatedConsoleSink(configOptions.Logging.ConsoleOutputTemplate),
                                levelSwitch: LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Console));

        ConfigureFileLogging(loggerConfiguration);
        ConfigureApplicationInsightsLogging(loggerConfiguration);

        Log.Logger = loggerConfiguration.CreateLogger();

        log = Log.ForContext<Program>();
    }

    /// <summary>
    ///     Adds the rolling-file sink when a file path is configured. A missing or invalid path
    ///     disables the sink rather than failing start-up.
    /// </summary>
    /// <param name="loggerConfiguration">The logger configuration to add the sink to.</param>
    private static void ConfigureFileLogging(LoggerConfiguration loggerConfiguration)
    {
        var fileOptions = configOptions.Logging.File;

        try
        {
            // fall back to the console template when none is configured for the file sink
            var outputTemplate = string.IsNullOrWhiteSpace(fileOptions.OutputTemplate)
                ? configOptions.Logging.ConsoleOutputTemplate
                : fileOptions.OutputTemplate;

            RollingInterval rollingInterval;
            string resolvedPath;

            if (string.IsNullOrWhiteSpace(fileOptions.Path))
            {
                // no explicit config → default to a one-file-per-run log named after the RunID,
                // co-located with the spreadsheet output. Rolling is unnecessary because each run
                // already has a unique filename.
                resolvedPath = Path.Combine(effectiveOutputDirectory, $"FileUsers-{runId}.log");
                rollingInterval = RollingInterval.Infinite;
            }
            else
            {
                // explicit config → honor it. Bare filenames land in the output directory; rooted
                // or directory-prefixed paths are honored verbatim (see ResolveFileLogPath).
                resolvedPath = ResolveFileLogPath(fileOptions.Path);

                rollingInterval = Enum.TryParse<RollingInterval>(fileOptions.RollingInterval, ignoreCase: true, out var parsed)
                    ? parsed
                    : RollingInterval.Day;
            }

            var fileFormatter = new LevelSeparatedFormatter(new MessageTemplateTextFormatter(outputTemplate));

            loggerConfiguration.WriteTo.File(
                formatter: fileFormatter,
                path: resolvedPath,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: fileOptions.RetainedFileCountLimit,
                levelSwitch: LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.File));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"File logging is disabled due to an invalid configuration: {ex.Message}");
        }
    }

    /// <summary>
    ///     Resolves an explicitly-configured log-file path against the spreadsheet output folder
    ///     when the configured path is just a filename. If the configured path is rooted (absolute)
    ///     or already has a directory component, it is honored verbatim as an explicit user override;
    ///     otherwise the log filename is joined with the effective output directory.
    /// </summary>
    /// <param name="configuredPath">
    ///     The raw <c>ApplicationLogging:File:Path</c> value from configuration.
    /// </param>
    /// <returns>The path handed to the Serilog file sink.</returns>
    private static string ResolveFileLogPath(string configuredPath)
    {
        // any user-supplied path with a directory component (relative or absolute) is honored as-is
        if (Path.IsPathRooted(configuredPath) || !string.IsNullOrEmpty(Path.GetDirectoryName(configuredPath)))
        {
            return configuredPath;
        }

        // bare filename → resolve against the effective spreadsheet output folder
        return Path.Combine(effectiveOutputDirectory, configuredPath);
    }

    /// <summary>
    ///     Returns the directory where the spreadsheet output will be written for this run, using
    ///     precedence: the CLI-provided output path's directory (if any), then <c>Output:FilePath</c>'s
    ///     directory, then <c>Output:Directory</c>, then the current working directory. Called once
    ///     early in <see cref="Main" /> so every derived artifact path (log file, RunID collision
    ///     check, default spreadsheet location) agrees on where the run's artifacts live.
    /// </summary>
    /// <param name="commandLineOutputPath">The value of <c>--output</c>, if provided.</param>
    /// <returns>An absolute-or-relative directory path; never <c>null</c> or empty.</returns>
    private static string ResolveOutputDirectory(string? commandLineOutputPath)
    {
        if (!string.IsNullOrWhiteSpace(commandLineOutputPath))
        {
            var directoryFromCli = Path.GetDirectoryName(commandLineOutputPath);

            if (!string.IsNullOrEmpty(directoryFromCli))
            {
                return directoryFromCli;
            }
        }

        var outputOptions = configOptions.Output;

        if (!string.IsNullOrWhiteSpace(outputOptions.FilePath))
        {
            var directoryFromFilePath = Path.GetDirectoryName(outputOptions.FilePath);

            if (!string.IsNullOrEmpty(directoryFromFilePath))
            {
                return directoryFromFilePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(outputOptions.Directory))
        {
            return outputOptions.Directory;
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    ///     Adds the Application Insights sink when a connection string is configured. A missing or
    ///     invalid connection string disables the sink rather than failing start-up.
    /// </summary>
    /// <param name="loggerConfiguration">The logger configuration to add the sink to.</param>
    private static void ConfigureApplicationInsightsLogging(LoggerConfiguration loggerConfiguration)
    {
        var connectionString = configOptions.Logging.ApplicationInsights.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
            telemetryConfiguration.ConnectionString = connectionString;

            loggerConfiguration.WriteTo.ApplicationInsights(
                telemetryConfiguration,
                TelemetryConverter.Traces,
                LoggingHelper.GetLogEventLevel(configOptions.Logging.LogLevel.ApplicationInsights),
                LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.ApplicationInsights));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Application Insights logging is disabled due to an invalid connection string: {ex.Message}");
        }
    }

    //// ****************************************************************************************
    //// application execution
    //// ****************************************************************************************

    private static async Task ProduceFileUserListAsync(ProduceFileUserListOptions options)
    {
        using var property = LogContext.PushProperty(LogContextProperties.ExecutionMode, Constants.ExecutionModes.ProduceFileUserList);
        using var cancellationTokenSource = new CancellationTokenSource();

        // In-flight CaseWare COM calls cannot be interrupted mid-flight, so the pipeline can only
        // wind down once each worker's current file finishes or throws. Log an immediate warning
        // in the Ctrl+C handler itself so the user knows their keypress was received while the
        // wind-down is in progress. The Warning level also triggers the blank-line separator, so
        // it stands out in the log. A second Ctrl+C escalates to an OS-level termination — the
        // escape hatch when the graceful shutdown itself is stuck on a native call.
        var cancellationAcknowledged = 0;
        void CancelKeyPressHandler(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            var pressCount = Interlocked.Increment(ref cancellationAcknowledged);

            if (pressCount == 1)
            {
                eventArgs.Cancel = true;

                log.Warning(
                    "Cancellation requested (Ctrl+C) — finishing in-flight file(s) and shutting down. Files that already completed are preserved in the journal; resume with --resume {RunId}. Press Ctrl+C again to abort immediately.",
                    runId);

                // ReSharper disable once AccessToDisposedClosure -- the handler is unsubscribed in the finally below before the token source is disposed
                cancellationTokenSource.Cancel();
            }
            else
            {
                // Second (or subsequent) Ctrl+C: the graceful shutdown is stuck (typically on an
                // in-flight CaseWare COM call that the CLR can't interrupt). Take Cancel=true so
                // the runtime's default handler cannot try to shut down politely — that path
                // waits for foreground threads and finalizers, which is exactly the hang the user
                // is trying to escape — and then terminate at the OS level via TerminateProcess.
                //
                // The journal is flushed to disk on every per-file append, so processing state
                // is preserved verbatim; a subsequent --resume against this RunID picks up right
                // where the abort landed. Console output is written synchronously by the sink so
                // the "aborting immediately" line reaches the terminal before we die. The file
                // log may miss the very last line if its buffer hasn't been flushed, which is an
                // acceptable trade for guaranteed prompt termination.
                eventArgs.Cancel = true;

                log.Warning("Second cancellation requested — aborting immediately.");

                Process.GetCurrentProcess().Kill();
            }
        }

        Console.CancelKeyPress += CancelKeyPressHandler;

        try
        {
            var serviceProvider = services.BuildServiceProvider();

            // Configuration phase — validate registered options and log warnings for any optional
            // configuration that is not populated. Options validation happens explicitly because
            // there is no generic host present to run ValidateOnStart automatically.
            PhaseScope.Run(
                "Configuration",
                () =>
                {
                    serviceProvider.GetRequiredService<IStartupValidator>().Validate();
                    LogOptionalConfigurationWarnings();
                });

            // Database connectivity phase — verify the CaseWare File Management database is reachable
            // before any per-file work begins so a misconfigured connection string fails fast with a
            // clean message instead of crashing partway through a run.
            var databaseReachable = await PhaseScope.RunAsync(
                "Database connectivity",
                () => ProbeDatabaseConnectivityAsync(serviceProvider, cancellationTokenSource.Token)).ConfigureAwait(false);

            if (!databaseReachable)
            {
                exitCode = -1;

                return;
            }

            using var scope = serviceProvider.CreateScope();
            var fileUserService = scope.ServiceProvider.GetRequiredService<IFileUserService>();

            await fileUserService.RunAsync(options.InputFilesPath, options.OutputPath, cancellationTokenSource.Token);
        }
        finally
        {
            // unsubscribe before the token source is disposed so the handler cannot outlive it
            Console.CancelKeyPress -= CancelKeyPressHandler;
        }
    }

    /// <summary>
    ///     Verifies connectivity to the CaseWare File Management database before any work begins.
    ///     A failure is logged as a clean message that names the connection-string field and hints
    ///     at <c>TrustServerCertificate=True</c> for on-prem servers using self-signed certificates;
    ///     no stack trace is emitted.
    /// </summary>
    /// <param name="serviceProvider">The configured service provider.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if the database is reachable; <c>false</c> otherwise.</returns>
    private static async Task<bool> ProbeDatabaseConnectivityAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        log.Information("Verifying connectivity to the CaseWare File Management database...");

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CaseWareFileManagementDbContext>();

        try
        {
            if (await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                log.Information("CaseWare File Management database connectivity verified.");

                return true;
            }

            log.Fatal(
                "Could not connect to the CaseWare File Management database. " +
                "Check ConnectionStrings:CaseWareFileManagement. " +
                "If the SQL Server uses a self-signed certificate, add TrustServerCertificate=True to the connection string.");

            return false;
        }
        catch (Exception ex)
        {
            log.Fatal(
                "Could not connect to the CaseWare File Management database: {Reason:l}. " +
                "Check ConnectionStrings:CaseWareFileManagement. " +
                "If the SQL Server uses a self-signed certificate, add TrustServerCertificate=True to the connection string.",
                ex.Message);

            return false;
        }
    }

    /// <summary>
    ///     Writes a warning to the console and the logger for each optional configuration value that
    ///     is not provided. These do not stop processing, but the reduced behavior is noted.
    /// </summary>
    private static void LogOptionalConfigurationWarnings()
    {
        if (string.IsNullOrWhiteSpace(configOptions.Logging.ApplicationInsights.ConnectionString))
        {
            log.Warning("Application Insights connection string is not configured; telemetry will not be sent.");
        }

        if (string.IsNullOrWhiteSpace(configOptions.ActiveDirectory.DomainName) ||
            string.IsNullOrWhiteSpace(configOptions.ActiveDirectory.CaseWareSupportTeamGroupName))
        {
            log.Warning("Active Directory is not fully configured; CaseWare support users will not be removed from the results.");
        }
    }
}
