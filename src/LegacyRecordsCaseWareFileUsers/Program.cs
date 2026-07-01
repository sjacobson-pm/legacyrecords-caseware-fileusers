using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using LegacyRecordsCaseWareFileUsers.CommandLineOptions;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using LegacyRecordsCaseWareFileUsers.Helpers.Extensions;
using LegacyRecordsCaseWareFileUsers.Logging;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;
using Serilog.Context;
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
    private static int exitCode;

    private static async Task Main(string[] args)
    {
        exitCode = 0;

        try
        {
            BindConfigurationOptions();
            ConfigureServices();
            ConfigureLogging();

            log.Information("Starting {ApplicationTitle} with arguments {Arguments}", Constants.ApplicationTitle, JsonConvert.SerializeObject(args));

            await Parser.Default.ParseArguments<ProduceFileUserListOptions>(args)
                        .MapResult(
                             async options => await ProduceFileUserListAsync(options),
                             async errors => await HandleCommandLineParsingErrorsAsync(args, errors));
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

        log.Information("Exiting {ApplicationTitle} with exit code {ExitCode}...", Constants.ApplicationTitle, exitCode);
        await Log.CloseAndFlushAsync();

        WaitForExitKeyPress();

        Environment.Exit(exitCode);
    }

    /// <summary>
    ///     Pauses before exit so output remains visible when the program is run interactively.
    /// </summary>
    private static void WaitForExitKeyPress()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        Console.WriteLine("Press any key to exit...");
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
        services.AddConsoleAppServices(configOptions);
    }

    /// <summary>
    ///     Configure logging.
    /// </summary>
    private static void ConfigureLogging()
    {
        var loggerConfiguration = new LoggerConfiguration();

        loggerConfiguration.MinimumLevel.ControlledBy(LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Default))
                           .MinimumLevel.Override("Microsoft", LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Microsoft))
                           .MinimumLevel.Override("System", LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.System))
                           .Enrich.FromLogContext()
                           .Enrich.WithProcessId()
                           .Enrich.WithMachineName()
                           .Enrich.WithProperty(LogContextProperties.EngineInstance, EngineInstanceId)
                           .WriteTo.Debug(
                                outputTemplate: configOptions.Logging.DebugOutputTemplate,
                                levelSwitch: LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Debug))
                           .WriteTo.Console(
                                outputTemplate: configOptions.Logging.ConsoleOutputTemplate,
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

        if (string.IsNullOrWhiteSpace(fileOptions.Path))
        {
            return;
        }

        try
        {
            // fall back to the console template when none is configured for the file sink
            var outputTemplate = string.IsNullOrWhiteSpace(fileOptions.OutputTemplate)
                ? configOptions.Logging.ConsoleOutputTemplate
                : fileOptions.OutputTemplate;

            // default to a daily rolling interval when the configured value is blank or invalid
            var rollingInterval = Enum.TryParse<RollingInterval>(fileOptions.RollingInterval, ignoreCase: true, out var parsed)
                ? parsed
                : RollingInterval.Day;

            loggerConfiguration.WriteTo.File(
                path: fileOptions.Path,
                outputTemplate: outputTemplate,
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

        void CancelKeyPressHandler(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;

            // ReSharper disable once AccessToDisposedClosure -- the handler is unsubscribed in the finally below before the token source is disposed
            cancellationTokenSource.Cancel();
        }

        Console.CancelKeyPress += CancelKeyPressHandler;

        try
        {
            var serviceProvider = services.BuildServiceProvider();

            // no generic host is present to run ValidateOnStart automatically, so trigger the
            // registered configuration validation explicitly before doing any work
            serviceProvider.GetRequiredService<IStartupValidator>().Validate();

            LogOptionalConfigurationWarnings();

            // verify database connectivity before any work begins so a misconfigured connection
            // string (for example, a self-signed certificate that needs TrustServerCertificate=True)
            // fails fast with a clean message instead of crashing partway through a run
            if (!await ProbeDatabaseConnectivityAsync(serviceProvider, cancellationTokenSource.Token).ConfigureAwait(false))
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
