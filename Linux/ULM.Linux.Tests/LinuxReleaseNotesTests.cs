using Xunit;

namespace ULM.Linux.Tests;

public class LinuxReleaseNotesTests
{
    [Theory]
    [InlineData("", "2.45.0", false)]
    [InlineData("2.45.0", "2.45.0", false)]
    [InlineData("2.44.0", "2.45.0", true)]
    [InlineData("2.46.0", "2.45.0", true)]
    public void NoticeOnlyForExistingInstallWithChangedVersion(string previous, string current, bool expected)
        => Assert.Equal(expected, LinuxReleaseNotes.ShouldShow(previous, current));

    [Fact]
    public void NotesDescribeLinuxFeaturesInBothLanguages()
    {
        Assert.Contains("DistroWatch", LinuxReleaseNotes.GetNotes(true));
        Assert.Contains("Preview", LinuxReleaseNotes.GetNotes(false));
        Assert.NotEqual(LinuxReleaseNotes.GetNotes(true), LinuxReleaseNotes.GetNotes(false));
    }
}
