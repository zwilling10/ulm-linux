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

        // Nutzerfund (2026-09-04): Checkbox angehakt, "Herunterladen" geklickt -> "Bitte mindestens
        // eine Distro markieren". Ursache: die Zeilen-Checkbox war an ein eigenes, lokales
        // LinuxIsoRow._isSelected-Feld gebunden statt an Entry.IsSelected — DownloadQueueAsync liest
        // aber _db.Entries.Where(e => e.IsSelected), sah die Checkbox also nie. Windows-Pendant
        // (IsoEntryViewModel.IsSelected, ViewModels/IsoViewModels.cs Zeile ~27) delegiert direkt an
        // _entry.IsSelected — dieselbe Proxy-Regel gilt jetzt auch hier.
        [Fact]
        public void IsSelected_ProxiesToUnderlyingEntry()
        {
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger" };
            var row = new LinuxIsoRow(entry, "/tmp/does-not-exist");

            row.IsSelected = true;

            Assert.True(entry.IsSelected);
        }

        [Fact]
        public void IsSelected_ReflectsPreExistingEntryState()
        {
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", IsSelected = true };
            var row = new LinuxIsoRow(entry, "/tmp/does-not-exist");

            Assert.True(row.IsSelected);
        }
    }
}
