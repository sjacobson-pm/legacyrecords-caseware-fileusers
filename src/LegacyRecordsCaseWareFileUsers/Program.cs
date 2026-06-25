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
using ILogger = Serilog.ILogger;

[assembly: InternalsVisibleTo("LegacyRecordsCaseWareFileUsers.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace LegacyRecordsCaseWareFileUsers;

[ExcludeFromCodeCoverage(Justification = "The main program class is not tested.")]
internal class Program
{
    public static readonly Guid EngineInstanceId = Guid.NewGuid();

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

            log.Information("Starting {applicationTitle} with arguments {arguments}", Constants.ApplicationTitle, JsonConvert.SerializeObject(args));

            await Parser.Default.ParseArguments<ProduceFileUserListOptions>(args)
                        .MapResult(
                             async options => await ProduceFileUserListAsync(options),
                             async errors => await HandleCommandLineParsingErrorsAsync(args, errors));
        }
        catch (Exception ex)
        {
            if (log == null)
            {
                throw;
            }

            const string Message = "An unhandled exception has occurred!";

            log.Fatal(ex, Message);

            exitCode = -1;
        }

        log.Information("Exiting {applicationTitle} with exit code {exitCode}...", Constants.ApplicationTitle, exitCode);
        await Log.CloseAndFlushAsync();

        Environment.Exit(exitCode);
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
            log.Error("The following command line parsing errors occurred: {@commandLineParsingErrors}", errorsList);

            await Task.CompletedTask;

            exitCode = -1;
        }
        else
        {
            log.Information("Help was requested: {helpArgs}", commandLineArgs);
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
        var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.ConnectionString = configOptions.Logging.ApplicationInsights.ConnectionString;

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
                                levelSwitch: LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.Console))
                           .WriteTo.ApplicationInsights(
                                telemetryConfiguration,
                                TelemetryConverter.Traces,
                                LoggingHelper.GetLogEventLevel(configOptions.Logging.LogLevel.ApplicationInsights),
                                LoggingHelper.GetLoggingLevelSwitch(configOptions.Logging.LogLevel.ApplicationInsights));

        Log.Logger = loggerConfiguration.CreateLogger();

        log = Log.ForContext<Program>();
    }

    //// ****************************************************************************************
    //// application execution
    //// ****************************************************************************************

    private static async Task ProduceFileUserListAsync(ProduceFileUserListOptions options)
    {
        using (LogContext.PushProperty(LogContextProperties.ExecutionMode, Constants.ExecutionModes.ProduceFileUserList))
        {
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
    }
}
