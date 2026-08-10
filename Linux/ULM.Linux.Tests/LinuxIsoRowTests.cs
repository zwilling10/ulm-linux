using System;
using System.IO;
using ULM.Core.Models;
using ULM.Linux.ViewModels;
using Xunit;

namespace ULM.Linux.Tests
{
    public class LinuxIsoRowTests
    {
        [Fact]
        public void CategoryLabel_TranslatesInternalKey()
        {
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger" };
            var row = new LinuxIsoRow(entry, "/tmp/does-not-exist");
            Assert.Equal(Constants.CategoryLabel("Einsteiger"), row.CategoryLabel);
        }

        [Fact]
        public void StatusLabel_NotLocal_WhenFileMissing()
        {
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" };
            var row = new LinuxIsoRow(entry, Path.Combine(Path.GetTempPath(), $"ulm-row-test-{Guid.NewGuid():N}"));
            Assert.Equal(ULM.Infrastructure.LocalizationService.T(ULM.Infrastructure.Str.Row_NotLocal), row.StatusLabel);
        }

        [Fact]
        public void SizeLabel_IsDashWhenFileMissing()
        {
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" };
            var row = new LinuxIsoRow(entry, Path.Combine(Path.GetTempPath(), $"ulm-row-test-{Guid.NewGuid():N}"));
            Assert.Equal("-", row.SizeLabel);
        }
    }
}
