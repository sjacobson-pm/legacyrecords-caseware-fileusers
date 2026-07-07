using System.Diagnostics.CodeAnalysis;
using LegacyRecordsCaseWareFileUsers.Data.Contexts;
using LegacyRecordsCaseWareFileUsers.Data.Repositories;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanteMoran.CaseWare;

namespace LegacyRecordsCaseWareFileUsers.Helpers.Extensions;

/// <summary>
///     Service registration extensions for the console application.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the database contexts, repositories, application services, and CaseWare
    ///     integration required by the console application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configOptions">The bound configuration options.</param>
    /// <param name="runId">
    ///     The run identifier chosen at startup by <see cref="Helpers.RunIdGenerator" />. Registered
    ///     as an <see cref="IRunContext" /> singleton so every collaborator observes the same value.
    /// </param>
    /// <param name="effectiveOutputDirectory">
    ///     The directory where the run's artifacts (spreadsheet, log, journal) will be written.
    ///     Registered as part of <see cref="IRunContext" /> so every collaborator agrees on it.
    /// </param>
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddConsoleAppServices(this IServiceCollection services, ConfigurationOptions configOptions, string runId, string effectiveOutputDirectory)
    {
        // run context (shared by every collaborator that needs the RunID and output directory)
        services.AddSingleton<IRunContext>(new RunContext(runId, effectiveOutputDirectory));

        // run journal — one per run, holds a SemaphoreSlim + open-file handle logic, so singleton
        services.AddSingleton<IRunJournal, RunJournal>();

        // database context (the shared CaseWare File Management database)
        services.AddDbContext<CaseWareFileManagementDbContext>(o => o.UseSqlServer(configOptions.ConnectionStrings.CaseWareFileManagement));

        // repositories
        services.AddScoped<ICaseWareFilesForApplicationsRepository, CaseWareFilesForApplicationsRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();

        // application services
        services.AddScoped<IInputReader, InputReader>();
        services.AddScoped<IWorkspaceManager, WorkspaceManager>();
        services.AddScoped<ICaseWareFileUserRetriever, CaseWareFileUserRetriever>();

        // SupportUserFilter caches the support-team AD membership for the lifetime of the instance,
        // so register it as a singleton to share that cache across the whole run
        services.AddSingleton<ISupportUserFilter, SupportUserFilter>();
        services.AddScoped<IFileUserSpreadsheetWriter, FileUserSpreadsheetWriter>();
        services.AddScoped<IFileUserService, FileUserService>();

        // PlanteMoran.CaseWare COM integration (requires Working Papers on the host at runtime)
        services.AddPlanteMoranCaseWareTransient();

        return services;
    }
}
