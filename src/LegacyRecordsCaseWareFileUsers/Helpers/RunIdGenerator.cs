using System;
using System.IO;
using System.Security.Cryptography;

namespace LegacyRecordsCaseWareFileUsers.Helpers;

/// <summary>
///     Generates a memorable run identifier of the form <c>{adjective}-{animal}-{verb}</c>, drawn
///     uniformly at random from the curated word lists in <see cref="RunIdWords" />.
///     <para>
///         The generator collision-checks against artifacts already present in the effective output
///         directory (the run's <c>.xlsx</c>, <c>.journal.jsonl</c>, <c>.journal.completed.jsonl</c>,
///         and <c>.log</c> siblings) so that a fresh run never silently overwrites an existing run's
///         artifacts. Cryptographic randomness is used to make the sequence unpredictable across
///         parallel invocations on the same host — two runs kicked off in the same second on the
///         same machine will pick different words.
///     </para>
/// </summary>
internal static class RunIdGenerator
{
    // Bounded so a pathological collision loop (a directory somehow full of collisions) surfaces
    // as a fast, loud failure rather than a runaway. 32 tries against millions of unique IDs is a
    // ridiculous safety margin — the expected retry count is effectively zero.
    private const int MaxAttempts = 32;

    /// <summary>
    ///     Generates a unique run identifier for the supplied output directory. If the directory
    ///     does not yet exist it is treated as empty (any generated ID is fine).
    /// </summary>
    /// <param name="outputDirectory">
    ///     The directory in which the run's artifacts (<c>FileUsers-{RunID}.xlsx</c>,
    ///     <c>FileUsers-{RunID}.journal.jsonl</c>, etc.) will be written. Used only to detect
    ///     collisions with prior runs.
    /// </param>
    /// <returns>
    ///     A lowercase, hyphen-separated identifier of the form <c>{adjective}-{animal}-{verb}</c>.
    /// </returns>
    public static string GenerateUnique(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = GenerateOne();

            if (!ArtifactsExistFor(outputDirectory, candidate))
            {
                return candidate;
            }
        }

        // Effectively unreachable at the configured word-list sizes; treated as a fail-loud
        // condition rather than an infinite loop so we notice if something is wrong.
        throw new InvalidOperationException(
            $"Could not generate a unique run identifier after {MaxAttempts} attempts. " +
            $"Inspect {outputDirectory} for an unusually large number of run artifacts.");
    }

    /// <summary>
    ///     Generates a single run identifier without checking for collisions. Exposed for tests
    ///     and for use in code paths where the caller has already established uniqueness.
    /// </summary>
    /// <returns>A candidate identifier.</returns>
    public static string GenerateOne()
    {
        var adjective = RunIdWords.Adjectives[NextIndex(RunIdWords.Adjectives.Length)];
        var animal = RunIdWords.Animals[NextIndex(RunIdWords.Animals.Length)];
        var verb = RunIdWords.Verbs[NextIndex(RunIdWords.Verbs.Length)];

        return $"{adjective}-{animal}-{verb}";
    }

    /// <summary>
    ///     Returns <c>true</c> if any of the four artifact file names for the given RunID already
    ///     exist in the target directory. A non-existent directory returns <c>false</c>.
    /// </summary>
    /// <param name="outputDirectory">The directory to inspect.</param>
    /// <param name="runId">The candidate RunID whose artifact names are checked.</param>
    /// <returns><c>true</c> if any artifact for the candidate already exists; otherwise <c>false</c>.</returns>
    private static bool ArtifactsExistFor(string outputDirectory, string runId)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return false;
        }

        var stem = $"FileUsers-{runId}";

        // spreadsheet + journal (in-flight and completed forms) + log file
        return File.Exists(Path.Combine(outputDirectory, $"{stem}.xlsx"))
            || File.Exists(Path.Combine(outputDirectory, $"{stem}.journal.jsonl"))
            || File.Exists(Path.Combine(outputDirectory, $"{stem}.journal.completed.jsonl"))
            || File.Exists(Path.Combine(outputDirectory, $"{stem}.log"));
    }

    /// <summary>
    ///     Returns a uniformly random non-negative integer strictly less than <paramref name="exclusiveMax" />,
    ///     using a cryptographic RNG so that concurrent processes on the same host do not collide by
    ///     seeding on system time.
    /// </summary>
    /// <param name="exclusiveMax">The exclusive upper bound of the random value.</param>
    /// <returns>A random index in the range <c>[0, exclusiveMax)</c>.</returns>
    private static int NextIndex(int exclusiveMax)
    {
        return RandomNumberGenerator.GetInt32(exclusiveMax);
    }
}
