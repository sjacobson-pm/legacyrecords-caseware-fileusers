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
    /// <returns>The same service collection, to allow chaining.</returns>
    public static IServiceCollection AddConsoleAppServices(this IServiceCollection services, ConfigurationOptions configOptions)
    {
        // database contexts (one per database / connection string)
        services.AddDbContext<CaseWareFilesDbContext>(o => o.UseSqlServer(configOptions.ConnectionStrings.CaseWareFileManagement));
        services.AddDbContext<StaffDbContext>(o => o.UseSqlServer(configOptions.ConnectionStrings.CaseWareUsers));

        // repositories
        services.AddScoped<IFilePathRepository, FilePathRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();

        // application services
        services.AddScoped<IInputReader, InputReader>();
        services.AddScoped<IWorkspaceManager, WorkspaceManager>();
        services.AddScoped<ICaseWareFileUserRetriever, CaseWareFileUserRetriever>();
        services.AddScoped<ISupportUserFilter, SupportUserFilter>();
        services.AddScoped<IFileUserSpreadsheetWriter, FileUserSpreadsheetWriter>();
        services.AddScoped<IFileUserService, FileUserService>();

        // PlanteMoran.CaseWare COM integration (requires Working Papers on the host at runtime)
        services.AddPlanteMoranCaseWareTransient();

        return services;
    }
}
