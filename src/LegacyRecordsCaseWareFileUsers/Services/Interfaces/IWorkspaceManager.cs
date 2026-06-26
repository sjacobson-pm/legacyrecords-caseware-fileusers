namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Manages the local workspace: the shared root directory for a run and the isolated per-file
///     workspaces created beneath it.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    ///     Creates the workspace root directory in preparation for a run.
    /// </summary>
    void PrepareWorkspaceRoot();

    /// <summary>
    ///     Creates a new, unique workspace directory beneath the root, dedicated to a single file so
    ///     concurrent files do not interfere with one another.
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
    ///     Deletes a single per-file workspace directory and its contents, swallowing cleanup errors.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace directory to delete.</param>
    void DeleteWorkspace(string workspaceDirectory);

    /// <summary>
    ///     Deletes the workspace root and everything beneath it, swallowing cleanup errors.
    /// </summary>
    void CleanUpWorkspaceRoot();
}
