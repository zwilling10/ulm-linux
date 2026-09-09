using System;
using Xunit;
namespace ULM.Linux.Tests;
public class LinuxPreferencesTests
{
    [Fact]
    public void DesktopEntryQuotesFieldCodes()
    {
        var entry = LinuxAutostart.BuildDesktopEntry("/opt/my app/test%app", Array.Empty<string>());
        Assert.Contains("Exec=\"/opt/my app/test%%app\"", entry);
        Assert.Contains("Terminal=false", entry);
        Assert.DoesNotContain("sh -c", entry);
    }
    [Fact]
    public void AutostartAndPreferencesRoundTripInsideProject()
    {
        string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "preferences-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            string entry = System.IO.Path.Combine(directory, "ulm.desktop");
            LinuxAutostart.SetEnabled(true, entry, "/opt/ULM/ulm-linux", Array.Empty<string>());
            Assert.Contains("Exec=\"/opt/ULM/ulm-linux\"", System.IO.File.ReadAllText(entry));
            LinuxAutostart.SetEnabled(false, entry);
            Assert.False(System.IO.File.Exists(entry));
            string ini = System.IO.Path.Combine(directory, "settings.ini");
            new LinuxPreferences { Theme = "Light", ExpertMode = false }.Save(ini);
            Assert.Equal("Light", LinuxPreferences.Load(ini).Theme);
            Assert.False(LinuxPreferences.Load(ini).ExpertMode);
        }
        finally { System.IO.Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("/opt/a\nb")]
    [InlineData("/opt/a\rb")]
    public void DesktopEntryRejectsNewlineInjection(string path) =>
        Assert.Throws<ArgumentException>(() => LinuxAutostart.BuildDesktopEntry(path, Array.Empty<string>()));
    [Theory]
    [InlineData("Light", "Light")]
    [InlineData("dark", "Dark")]
    [InlineData("invalid", "System")]
    public void ThemeIsNormalized(string value, string expected) =>
        Assert.Equal(expected, LinuxPreferences.NormalizeTheme(value));
}
