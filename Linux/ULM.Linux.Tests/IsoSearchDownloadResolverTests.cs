using System;
using System.IO;
using System.Linq;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.Views;
using Xunit;

namespace ULM.Linux.Tests;

public sealed class IsoSearchDownloadResolverTests
{
    [Fact]
    public void CreateEntryFromDiscovery_PreservesDistroWatchIdentity()
    {
        var d = new DiscoveredDistro
        {
            Name = "Q4OS",
            Slug = "q4os",
            SuggestedCategory = "Einsteiger",
            InfoKind = DiscoveryInfoKind.AddedOn,
            InfoArg1 = "2026-09-09"
        };

        var entry = IsoSearchDialog.CreateEntryFromDiscovery(d, "Fortgeschrittene");

        Assert.Equal("Q4OS", entry.Name);
        Assert.Equal("Fortgeschrittene", entry.Category);
        Assert.Equal("q4os", entry.DiscoverySlug);
        Assert.Equal("https://distrowatch.com/table.php?distribution=q4os", entry.DiscoveryPage);
    }

    [Fact]
    public void SaveThenLoad_PreservesDiscoveryMetadata()
    {
        AppPaths paths = AppPaths.Instance;
        IsoDatabaseService db = IsoDatabaseService.Instance;
        string originalBase = paths.BaseDirectory;
        var originalEntries = db.Entries.ToList();
        string tempDir = Path.Combine(Path.GetTempPath(), $"ulm-db-discovery-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            paths.SetPaths(tempDir);
            while (db.Count > 0) db.Remove(0);
            db.Add(new IsoEntry { Name = "Q4OS", DiscoverySlug = "q4os", DiscoveryPage = "https://distrowatch.com/table.php?distribution=q4os" });
            db.Save();
            db.Load();

            IsoEntry reloaded = db.Entries.Single(e => e.Name == "Q4OS");
            Assert.Equal("q4os", reloaded.DiscoverySlug);
            Assert.Equal("https://distrowatch.com/table.php?distribution=q4os", reloaded.DiscoveryPage);
        }
        finally
        {
            while (db.Count > 0) db.Remove(0);
            foreach (var e in originalEntries) db.Add(e);
            paths.SetPaths(originalBase);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Resolver_UsesDiscoveryMetadataAndCachesResolvedUrl()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../../Core/Services/HttpService.cs", AppContext.BaseDirectory));

        Assert.Contains("entry.DiscoveryPage", code);
        Assert.Contains("entry.DiscoverySlug", code);
        Assert.Contains("entry.Url = result.Item2", code);
    }

    [Fact]
    public void GitHubReleaseUrl_YieldsRepositorySlugForIsoSearchFallback()
    {
        string? repo = HttpService.TryFindGitHubRepositorySlug("https://github.com/crhy/spaced/releases/download/v9.26/spaced-linux-9.26-amd64.iso");

        Assert.Equal("crhy/spaced", repo);
    }

    [Fact]
    public void DownloadsSourceForgeProjectUrl_FansOutsMirrors()
    {
        var entry = new IsoEntry
        {
            Url = "https://downloads.sourceforge.net/project/pulsaros-inled/pulsaros-0.4-beta-bittenfruit-arch-grub-0.4-beta-bittenfruit.iso"
        };

        var result = entry.AllDownloadUrls().ToList();

        Assert.Equal("https://master.dl.sourceforge.net/project/pulsaros-inled/pulsaros-0.4-beta-bittenfruit-arch-grub-0.4-beta-bittenfruit.iso?viasf=1", result[0]);
        Assert.Contains(result, u => u.Contains("?use_mirror=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsoSearchButtonSaysTakeOverDownload()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/IsoSearchDialog.cs", AppContext.BaseDirectory));

        Assert.Contains("/ Download", code);
    }
}
