using System.Collections.Generic;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Retrieves the users assigned to a CaseWare file's FILE security group.
/// </summary>
public interface ICaseWareFileUserRetriever
{
    /// <summary>
    ///     Opens the (already local) CaseWare file, ensures protection is enabled, and returns the
    ///     CaseWare user identifiers assigned to the FILE security group.
    /// </summary>
    /// <param name="caseWareFilePath">The local path of the CaseWare .ac_ file.</param>
    /// <returns>The CaseWare user identifiers in the FILE security group.</returns>
    ICollection<string> GetFileSecurityGroupUserIdentifiers(string caseWareFilePath);
}
