using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;

namespace LegacyRecordsCaseWareFileUsers.Options;

/// <summary>
///     The root strongly-typed configuration options bound from appsettings.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public class ConfigurationOptions
{
    // Bound from a custom (Serilog-shaped) section rather than the standard "Logging" key so the
    // editor's Microsoft appsettings JSON schema does not flag the custom output-template settings.
    [ConfigurationKeyName("ApplicationLogging")]
    public LoggingOptions Logging { get; set; } = null!;

    public ConnectionStringOptions ConnectionStrings { get; set; } = new();

    public CaseWareOptions CaseWare { get; set; } = new();

    public ActiveDirectoryOptions ActiveDirectory { get; set; } = new();

    public WorkspaceOptions Workspace { get; set; } = new();

    public OutputOptions Output { get; set; } = new();

    public InputOptions Input { get; set; } = new();

    public ProcessingOptions Processing { get; set; } = new();
}
