using System.Linq;
using LegacyRecordsCaseWareFileUsers.Helpers;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Helpers;

public class RunIdWordsTests
{
    // Guards against accidentally introducing a duplicate word in one of the curated lists — a
    // duplicate silently biases the RunID distribution toward that word and shrinks the unique-ID
    // space. Cheap check with real value.
    [Theory]
    [InlineData("Adjectives")]
    [InlineData("Animals")]
    [InlineData("Verbs")]
    public void EachList_HasNoDuplicates(string listName)
    {
        var words = GetList(listName);

        var duplicates = words.GroupBy(w => w).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        duplicates.ShouldBeEmpty($"The '{listName}' list contains duplicate word(s): {string.Join(", ", duplicates)}");
    }

    [Theory]
    [InlineData("Adjectives")]
    [InlineData("Animals")]
    [InlineData("Verbs")]
    public void EachList_ContainsOnlyLowercaseAlphanumericWords(string listName)
    {
        var words = GetList(listName);

        foreach (var word in words)
        {
            word.ShouldNotBeNullOrWhiteSpace();
            word.ShouldMatch("^[a-z0-9]+$", $"Word '{word}' in '{listName}' is not lowercase alphanumeric.");
        }
    }

    [Fact]
    public void UniqueCombinations_Exceed_OneMillion()
    {
        // very loose lower bound: enough combinations that collisions are effectively impossible
        // for realistic run frequencies
        var combinations = (long)RunIdWords.Adjectives.Length
            * RunIdWords.Animals.Length
            * RunIdWords.Verbs.Length;

        combinations.ShouldBeGreaterThan(1_000_000);
    }

    private static string[] GetList(string listName) => listName switch
    {
        "Adjectives" => RunIdWords.Adjectives,
        "Animals" => RunIdWords.Animals,
        "Verbs" => RunIdWords.Verbs,
        _ => throw new System.ArgumentOutOfRangeException(nameof(listName), listName, "Unknown list name."),
    };
}
