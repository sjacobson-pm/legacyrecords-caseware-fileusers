using System.Collections.Generic;
using LegacyRecordsCaseWareFileUsers.Models;

namespace LegacyRecordsCaseWareFileUsers.Services.Interfaces;

/// <summary>
///     Reads and classifies the collection of inputs (UNC paths or file identifiers) to process.
/// </summary>
public interface IInputReader
{
    /// <summary>
    ///     Reads and classifies the inputs to process. When <paramref name="inputFilesPath" /> is
    ///     supplied, the inputs are read from that file (one per line); otherwise they are read from
    ///     the <c>Input:Files</c> configuration section.
    /// </summary>
    /// <param name="inputFilesPath">Optional path to a text file of inputs.</param>
    /// <returns>The parsed input items.</returns>
    IReadOnlyList<FileInputItem> ReadInputs(string? inputFilesPath);
}
