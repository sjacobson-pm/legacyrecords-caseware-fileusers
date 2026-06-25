namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Manages isolated local workspaces used to download and open individual CaseWare files.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    ///     Creates a new, unique workspace directory dedicated to a single file so concurrent or
    ///     sequential files do not interfere with one another.
    /// </summary>
    /// <returns>The full path of the created workspace directory.</returns>
    string CreateWorkspace();

    /// <summary>
    ///     Copies the source file into the supplied workspace directory.
    /// </summary>
    /// <param name="sourceUncPath">The UNC path of the file to copy.</param>
    /// <param name="workspaceDirectory">The destination workspace directory.</param>
    /// <returns>The full local path of the copied file.</returns>
    string CopyFileToWorkspace(string sourceUncPath, string workspaceDirectory);

    /// <summary>
    ///     Deletes a workspace directory and its contents, swallowing any cleanup errors.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace directory to delete.</param>
    void DeleteWorkspace(string workspaceDirectory);
}
