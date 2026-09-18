using ULM.Core.Models;
using ULM.Infrastructure;
using Xunit;

namespace ULM.Linux.Tests;

public sealed class DiscoveryInfoTests
{
    [Fact]
    public void Info_UsesCurrentLanguageForAddedOn()
    {
        var distro = new DiscoveredDistro
        {
            Name = "ThorOS",
            Slug = "thoros",
            InfoKind = DiscoveryInfoKind.AddedOn,
            InfoArg1 = "08-17",
        };

        LocalizationService.SetLanguage(AppLanguage.German);
        Assert.Contains("Hinzugefügt", distro.Info);

        LocalizationService.SetLanguage(AppLanguage.English);
        Assert.Contains("Added", distro.Info);
    }

    [Fact]
    public void Info_UsesCurrentLanguageForRankHits()
    {
        var distro = new DiscoveredDistro
        {
            Name = "CachyOS",
            Slug = "cachyos",
            InfoKind = DiscoveryInfoKind.RankHits,
            InfoArg1 = "1",
            InfoArg2 = "3955",
        };

        LocalizationService.SetLanguage(AppLanguage.German);
        Assert.Contains("Hits/Tag", distro.Info);

        LocalizationService.SetLanguage(AppLanguage.English);
        Assert.Contains("hits/day", distro.Info);
    }
}
