using System;
using System.Collections.Generic;
using System.IO;
using LegacyRecordsCaseWareFileUsers.Models;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyRecordsCaseWareFileUsers.Services.Implementations;

/// <summary>
///     Reads inputs from a text file or configuration and classifies each as a file identifier or a
///     UNC path.
/// </summary>
internal class InputReader : IInputReader
{
    private readonly ConfigurationOptions options;
    private readonly ILogger<InputReader> logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InputReader" /> class.
    /// </summary>
    /// <param name="optionsAccessor">The configuration options accessor.</param>
    /// <param name="logger">The logger.</param>
    public InputReader(IOptions<ConfigurationOptions> optionsAccessor, ILogger<InputReader> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = optionsAccessor.Value;
        this.logger = logger;
    }

    /// <summary>
    ///     Parses raw input lines into classified items. Blank lines are skipped; a line that parses
    ///     as an integer is treated as a file ID, otherwise it is treated as a UNC path.
    /// </summary>
    /// <param name="rawValues">The raw input lines.</param>
    /// <returns>The parsed input items.</returns>
    public static IReadOnlyList<FileInputItem> ParseItems(IEnumerable<string> rawValues)
    {
        ArgumentNullException.ThrowIfNull(rawValues);

        var items = new List<FileInputItem>();

        foreach (var rawValue in rawValues)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            var trimmed = rawValue.Trim();

            if (int.TryParse(trimmed, out var fileId))
            {
                items.Add(new FileInputItem(trimmed, fileId, null));
            }
            else
            {
                items.Add(new FileInputItem(trimmed, null, trimmed));
            }
        }

        return items;
    }

    /// <inheritdoc />
    public IReadOnlyList<FileInputItem> ReadInputs(string? inputFilesPath)
    {
        // precedence: --input-files command line argument, then Input:FilePath config, then the
        // inline Input:Files config array
        var resolvedPath = string.IsNullOrWhiteSpace(inputFilesPath)
            ? this.options.Input.FilePath
            : inputFilesPath;

        IEnumerable<string> rawValues;

        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException($"The input file '{resolvedPath}' does not exist.", resolvedPath);
            }

            this.logger.LogInformation("Reading inputs from file {InputFilesPath}...", resolvedPath);
            rawValues = File.ReadAllLines(resolvedPath);
        }
        else
        {
            this.logger.LogInformation("No input file supplied; reading inputs from the Input:Files configuration section...");
            rawValues = this.options.Input.Files;
        }

        var items = ParseItems(rawValues);

        this.logger.LogInformation("Parsed {ItemCount} input item(s).", items.Count);

        return items;
    }
}
