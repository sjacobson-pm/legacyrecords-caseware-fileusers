using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers;

/// <summary>
///     Application-wide constants.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "There is nothing to test in this class at this point.")]
public static class Constants
{
    public const string ApplicationName = "[fill-me-in]";
    public const string UtilityName = "[fill-me-in]";

    /// <summary>
    ///     The CaseWare security group whose assigned users are reported for each file.
    /// </summary>
    public const string FileSecurityGroupName = "FILE";

    public static string ApplicationTitle => $"{ApplicationName} - {UtilityName}";

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
