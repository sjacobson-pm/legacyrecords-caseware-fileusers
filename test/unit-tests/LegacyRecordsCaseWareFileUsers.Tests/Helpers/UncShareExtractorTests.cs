using LegacyRecordsCaseWareFileUsers.Helpers;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Helpers;

public class UncShareExtractorTests
{
    [Theory]
    [InlineData(@"\\network\volume\share\engagement\file.ac_", 3, @"\\network\volume\share")]
    [InlineData(@"\\network\volume\share_2\engagement\file.ac_", 3, @"\\network\volume\share_2")]
    [InlineData(@"\\network\volume\share\file.ac_", 3, @"\\network\volume\share")]
    [InlineData(@"\\network\volume\share\deep\deeper\deepest\file.ac_", 3, @"\\network\volume\share")]
    [InlineData(@"\\network\volume\share\engagement\file.ac_", 2, @"\\network\volume")]
    [InlineData(@"\\network\volume\share\engagement\file.ac_", 1, @"\\network")]
    public void Extract_HappyPath_ReturnsThePrefixUpToTheRequestedSegment(string uncPath, int segmentIndex, string expected)
    {
        UncShareExtractor.Extract(uncPath, segmentIndex).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Extract_NullOrWhitespacePath_ReturnsEmpty(string? uncPath)
    {
        UncShareExtractor.Extract(uncPath, 3).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(@"\\server\file.ac_", 3)] // only 2 segments — no third
    [InlineData(@"\\server\share", 3)] // exactly 2 segments (no filename component either)
    [InlineData(@"file.ac_", 3)] // no UNC structure at all
    public void Extract_PathShallowerThanIndex_ReturnsEmpty(string uncPath, int segmentIndex)
    {
        UncShareExtractor.Extract(uncPath, segmentIndex).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Extract_NonPositiveIndex_TreatedAsOne(int segmentIndex)
    {
        UncShareExtractor.Extract(@"\\network\volume\share\file.ac_", segmentIndex).ShouldBe(@"\\network");
    }

    [Fact]
    public void Extract_ForwardSlashesInPath_TreatedAsSeparatorsAndNormalizedToBackslashes()
    {
        // some tooling normalizes UNC paths to forward slashes; the extractor should still work
        // and normalize the output to backslashes so downstream operations don't have to consider
        // both separator styles
        UncShareExtractor.Extract("//network/volume/share/file.ac_", 3).ShouldBe(@"\\network\volume\share");
    }
}
