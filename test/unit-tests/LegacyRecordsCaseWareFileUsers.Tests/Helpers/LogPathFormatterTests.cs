using LegacyRecordsCaseWareFileUsers.Helpers;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Helpers;

public class LogPathFormatterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void FormatForLog_BlankInput_ReturnsEmptyString(string? input, string expected)
    {
        LogPathFormatter.FormatForLog(input).ShouldBe(expected);
    }

    [Fact]
    public void FormatForLog_BareFileName_ReturnsTheFileNameUnchanged()
    {
        LogPathFormatter.FormatForLog("engagement.ac_").ShouldBe("engagement.ac_");
    }

    [Fact]
    public void FormatForLog_FullUncPath_ReturnsParentFolderAndFileName()
    {
        LogPathFormatter.FormatForLog(@"\\fileserver\engagements\acme\engagement.ac_")
            .ShouldBe(@"acme\engagement.ac_");
    }

    [Fact]
    public void FormatForLog_LocalWorkspacePathWithGuidParent_ReturnsGuidAndFileName()
    {
        // the GUID parent is preserved on purpose so logs from concurrent workers can be told apart
        LogPathFormatter.FormatForLog(@"C:\Users\me\AppData\Local\Temp\lr-caseware-fileusers\abc123\acme.ac_")
            .ShouldBe(@"abc123\acme.ac_");
    }
}
