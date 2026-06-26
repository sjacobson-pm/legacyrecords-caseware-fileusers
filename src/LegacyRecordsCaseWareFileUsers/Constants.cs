using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers;

/// <summary>
///     Application-wide constants.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public static class Constants
{
    /// <summary>
    ///     The CaseWare security group whose assigned users are reported for each file.
    /// </summary>
    public const string FileSecurityGroupName = "FILE";

    /// <summary>
    ///     Gets the application title used in logging and console output.
    /// </summary>
    /// <value>The application title.</value>
    // TODO:: [sjacobson] 2026-06-25 - Replace this placeholder application name with the real
    // application name before using this in production.
    public static string ApplicationTitle => "Beholder";

    /// <summary>
    ///     The named execution modes used for logging context.
    /// </summary>
    public static class ExecutionModes
    {
        /// <summary>
        ///     The execution mode used when producing the file-user list spreadsheet.
        /// </summary>
        public const string ProduceFileUserList = "produce-file-user-list";
    }
}
