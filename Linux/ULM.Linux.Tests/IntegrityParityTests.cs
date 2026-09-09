using System.Linq;
using System.IO;
using ULM.Core.Models;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;
using Xunit;

namespace ULM.Linux.Tests;

[Collection("LinuxLocalizationCurrent")]
public class IntegrityParityTests
{
    [Fact]
    public void MissingCandidatesExcludeAbsentFiles()
    {
        var db = new FakeIsoDatabaseService();
        db.Add(new IsoEntry { Name = "Absent", Filename = "absent.iso", VerifiedComplete = true, UsbStatus = UsbStatus.Missing });
        var vm = new LinuxMainViewModel(db, ".");
        Assert.Empty(vm.GetVerifiedCompleteEntriesMissingFromStick());
        Assert.False(vm.CancelDownloadCommand.CanExecute(null));
    }

    [Fact]
    public void MissingCandidatesIncludeLocalIsoWithoutPersistedVerifiedFlag()
    {
        string downloadDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-local-copy-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(downloadDir);
        try
        {
            string filename = "spaced-linux-9.26-amd64.iso";
            using (var fs = System.IO.File.Create(System.IO.Path.Combine(downloadDir, filename)))
                fs.SetLength(Constants.MinIsoSizeBytes + 1_000_000);
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Spaced Linux", Filename = filename, UsbStatus = UsbStatus.Missing, VerifiedComplete = false });

            var vm = new LinuxMainViewModel(db, downloadDir);

            Assert.Contains(vm.GetVerifiedCompleteEntriesMissingFromStick(), e => e.Name == "Spaced Linux");
        }
        finally { try { System.IO.Directory.Delete(downloadDir, true); } catch { } }
    }

    [Fact]
    public void LocalMaintenanceTriggersMissingCopyOfferBeforeNoJunkReturn()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml.cs", AppContext.BaseDirectory));

        Assert.Contains("OfferMissingLocalCopiesForSelectedDrive", code);
        Assert.Contains("LocalizationService.T(Str.Log_NoJunkFound)", code);
    }

    [Fact]
    public void StickMaintenanceOffersAreDeduplicatedPerDriveAndFile()
    {
        var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), ".");

        Assert.True(vm.MarkCopyOffered("/media/ULM", "ubuntu.iso"));
        Assert.False(vm.MarkCopyOffered("/media/ULM", "ubuntu.iso"));
        Assert.True(vm.MarkIncompleteStickIsoOffered("/media/ULM", "ubuntu.iso"));
        Assert.False(vm.MarkIncompleteStickIsoOffered("/media/ULM", "ubuntu.iso"));
    }

    [Fact]
    public void ActivityHistoryCanBeCleared()
    {
        var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), ".");
        vm.ActivityHistory.Add("test");
        vm.ClearActivityHistoryCommand.Execute(null);
        Assert.Empty(vm.ActivityHistory);
    }

    [Fact]
    public async System.Threading.Tasks.Task VerifyStickIntegrityAsync_RecordsStartAndDoneInHistory()
    {
        var db = new FakeIsoDatabaseService();
        string stickDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-integrity-history-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(stickDir);
        try
        {
            string isoPath = System.IO.Path.Combine(stickDir, "ubuntu.iso");
            using (var fs = System.IO.File.Create(isoPath)) fs.SetLength(Constants.MinIsoSizeBytes + 1_000_000);
            var entry = new IsoEntry { Name = "Ubuntu", Filename = "ubuntu.iso", Sha256 = await IsoEntry.ComputeSha256Async(isoPath) };
            db.Add(entry);
            var vm = new LinuxMainViewModel(db, ".")
            {
                SelectedDrive = new LinuxBlockDevice(stickDir, stickDir, 400_000_000L, "Test", true),
            };

            await vm.VerifyStickIntegrityAsync();

            Assert.Contains(vm.ActivityHistory, h => h.Contains(LocalizationService.T(Str.Log_CheckingIntegrity)));
            Assert.Contains(vm.ActivityHistory, h => h.Contains(string.Format(LocalizationService.T(Str.Log_IsosVerifiedStatus), 1)));
            Assert.Equal(string.Format(LocalizationService.T(Str.Log_IsosVerifiedStatus), 1), vm.IntegrityStatus);
        }
        finally { System.IO.Directory.Delete(stickDir, true); }
    }
}
