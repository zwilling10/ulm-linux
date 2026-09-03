using System.Linq;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;
using Xunit;

namespace ULM.Linux.Tests
{
    [Collection("LinuxLocalizationCurrent")]
    public class LinuxMainViewModelTests
    {
        private static FakeIsoDatabaseService BuildDb()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger" });
            db.Add(new IsoEntry { Name = "Debian 12", Category = "Fortgeschrittene" });
            db.Add(new IsoEntry { Name = "Kali Linux 2025.1", Category = "Sicherheit" });
            return db;
        }

        [Fact]
        public void Constructor_LoadsAllEntriesIntoRows()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            Assert.Equal(3, vm.Rows.Count);
        }

        [Fact]
        public void Categories_StartsWithAllOption()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            Assert.Null(vm.Categories.First().Key);
            Assert.Equal(1 + Constants.Categories.Length, vm.Categories.Count);
        }

        [Fact]
        public void SelectedCategory_FiltersRows()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            vm.SelectedCategory = vm.Categories.First(c => c.Key == "Sicherheit");
            Assert.Single(vm.Rows);
            Assert.Equal("Kali Linux 2025.1", vm.Rows[0].Name);
        }

        [Fact]
        public void SearchText_FiltersRowsByNameCaseInsensitive()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            vm.SearchText = "debian";
            Assert.Single(vm.Rows);
            Assert.Equal("Debian 12", vm.Rows[0].Name);
        }

        [Fact]
        public void SearchText_AndCategory_CombineAsAndFilter()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            vm.SelectedCategory = vm.Categories.First(c => c.Key == "Einsteiger");
            vm.SearchText = "debian";
            Assert.Empty(vm.Rows);
        }

        [Fact]
        public void RefreshCommand_ReloadsFromDatabase()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            db.Add(new IsoEntry { Name = "Fedora Workstation 41", Category = "Fortgeschrittene" });
            vm.RefreshCommand.Execute(null);
            Assert.Equal(4, vm.Rows.Count);
        }

        [Fact]
        public void ToggleLanguageCommand_SwitchesCurrentLanguage()
        {
            string tempSettings = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-lang-{System.Guid.NewGuid():N}.ini");
            LocalizationService.Initialize(tempSettings);
            AppLanguage before = LocalizationService.Current;
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test", tempSettings);

            vm.ToggleLanguageCommand.Execute(null);

            Assert.NotEqual(before, LocalizationService.Current);
            System.IO.File.Delete(tempSettings);
        }

        private static LinuxMainViewModel.ResolveFunc FakeResolve(string version, string url, string filename) =>
            entry => System.Threading.Tasks.Task.FromResult((version, url, filename));

        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_NoUrl_SetsNoUrlStatusAndDoesNotCallDownload()
        {
            var db = BuildDb();
            bool downloadCalled = false;
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                (url, dest, progress, token) => { downloadCalled = true; return System.Threading.Tasks.Task.FromResult(true); },
                FakeResolve("", "", ""));
            vm.SelectedRow = vm.Rows.First();

            await vm.DownloadSelectedAsync();

            Assert.False(downloadCalled);
            Assert.Equal(LocalizationService.T(Str.Linux_Download_NoUrl), vm.DownloadStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_WithUrl_CallsDownloadAndReportsProgress()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                async (url, dest, progress, token) =>
                {
                    // Progress<T>.Report marshalt ueber SynchronizationContext.Current (hier: keiner
                    // im Testkontext) per ThreadPool.QueueUserWorkItem, feuert also asynchron — ein
                    // kurzer Delay laesst den Callback vor der folgenden Assertion durchlaufen.
                    progress?.Report((50, "50%"));
                    await System.Threading.Tasks.Task.Delay(50);
                    return true;
                },
                FakeResolve("24.04", "https://example.invalid/ubuntu.iso", "ubuntu.iso"));
            vm.SelectedRow = vm.Rows.First(r => r.Name == "Ubuntu 24.04 LTS");

            await vm.DownloadSelectedAsync();

            Assert.Equal(50, vm.DownloadPercent);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_AllUrlsFail_SetsFailedStatus()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                (url, dest, progress, token) => System.Threading.Tasks.Task.FromResult(false),
                FakeResolve("24.04", "https://example.invalid/ubuntu.iso", "ubuntu.iso"));
            vm.SelectedRow = vm.Rows.First(r => r.Name == "Ubuntu 24.04 LTS");

            await vm.DownloadSelectedAsync();

            Assert.Equal(LocalizationService.T(Str.Linux_Download_Failed), vm.DownloadStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task CopySelectedToStickAsync_NoDriveSelected_SetsNoStickStatus()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" });
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-copy-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(tempDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(tempDir, "ubuntu.iso"), new byte[1024]);
            var vm = new LinuxMainViewModel(db, tempDir);
            vm.SelectedRow = vm.Rows.First();

            await vm.CopySelectedToStickAsync();

            Assert.Equal(LocalizationService.T(Str.Linux_Copy_NoDrive), vm.CopyStatus);
            System.IO.Directory.Delete(tempDir, true);
        }

        [Fact]
        public async System.Threading.Tasks.Task CopySelectedToStickAsync_CopiesFileToCategoryFolder()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" });
            string downloadDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-copy-src-{System.Guid.NewGuid():N}");
            string stickDir    = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-copy-dst-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(downloadDir);
            System.IO.Directory.CreateDirectory(stickDir);
            byte[] content = new byte[2048];
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(downloadDir, "ubuntu.iso"), content);

            var vm = new LinuxMainViewModel(db, downloadDir);
            vm.SelectedRow = vm.Rows.First();
            // SelectedDrive wird direkt gesetzt, kein PollDrivesAsync()/echter lsblk-Aufruf noetig —
            // der Default-LinuxUsbService (echter Prozessaufruf) wird in diesem Test nie beruehrt.
            vm.SelectedDrive = new LinuxBlockDevice(stickDir, stickDir, 100_000_000L, "Test", true);

            await vm.CopySelectedToStickAsync();

            string expectedDest = System.IO.Path.Combine(stickDir, "Einsteiger", "ubuntu.iso");
            Assert.True(System.IO.File.Exists(expectedDest));
            Assert.Equal(content.Length, new System.IO.FileInfo(expectedDest).Length);

            System.IO.Directory.Delete(downloadDir, true);
            System.IO.Directory.Delete(stickDir, true);
        }

        [Fact]
        public void GitHubToken_LoadsFromIniOnConstruction()
        {
            string tempSettings = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-ghtoken-{System.Guid.NewGuid():N}.ini");
            IniService.Write(tempSettings, "App", "GitHubToken", "ghp_existing");

            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test", tempSettings);

            Assert.Equal("ghp_existing", vm.GitHubToken);
            System.IO.File.Delete(tempSettings);
        }

        [Fact]
        public void GitHubToken_Setter_PersistsToIniAndUpdatesHttpService()
        {
            string tempSettings = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-ghtoken-{System.Guid.NewGuid():N}.ini");
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test", tempSettings);

            vm.GitHubToken = "ghp_new_token";

            Assert.Equal("ghp_new_token", IniService.Read(tempSettings, "App", "GitHubToken", ""));
            Assert.Equal("ghp_new_token", HttpService.Instance.GitHubToken);
            System.IO.File.Delete(tempSettings);
        }
    }

    [Xunit.CollectionDefinition("LinuxLocalizationCurrent", DisableParallelization = true)]
    public class LinuxLocalizationCurrentCollection { }

    public class LinuxMainViewModelDrivesTests
    {
        [Fact]
        public async System.Threading.Tasks.Task PollDrivesAsync_PopulatesDrivesFromUsbService()
        {
            const string json = """{"blockdevices":[{"name":"sdb","size":16008609792,"model":"Test","rm":true,"type":"disk","mountpoint":null}]}""";
            var usbService = new LinuxUsbService(() => System.Threading.Tasks.Task.FromResult(json));
            var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), "/tmp/ulm-linux-vm-test", usbService: usbService);

            await vm.PollDrivesAsync();

            Assert.Single(vm.Drives);
            Assert.Equal("/dev/sdb", vm.Drives[0].DeviceNode);
        }

        [Fact]
        public async System.Threading.Tasks.Task PollDrivesAsync_ClearsSelectedDrive_WhenDeviceNoLongerPresent()
        {
            const string firstJson = """{"blockdevices":[{"name":"sdb","size":16008609792,"model":"Test","rm":true,"type":"disk","mountpoint":null}]}""";
            const string secondJson = """{"blockdevices":[]}""";
            int call = 0;
            var usbService = new LinuxUsbService(() =>
            {
                call++;
                return System.Threading.Tasks.Task.FromResult(call == 1 ? firstJson : secondJson);
            });
            var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), "/tmp/ulm-linux-vm-test", usbService: usbService);

            await vm.PollDrivesAsync();
            vm.SelectedDrive = vm.Drives[0];
            await vm.PollDrivesAsync();

            Assert.Null(vm.SelectedDrive);
        }
    }

    public class LinuxMainViewModelVentoyTests
    {
        private static LinuxMainViewModel BuildVmWithDrive(VentoyInstallService ventoyService)
        {
            var db = new FakeIsoDatabaseService();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", ventoyInstallService: ventoyService);
            vm.SelectedDrive = new LinuxBlockDevice("/dev/sdb", null, 16_000_000_000L, "Test Stick", false);
            return vm;
        }

        [Fact]
        public void SelectedDrive_Set_RaisesCanExecuteChangedForDependentCommands()
        {
            // Regression-Test fuer den Nutzerfund: RelayCommand.CanExecute() selbst wertet die
            // Bedingung IMMER live aus (auch ohne diesen Fix), das allein haette den Bug also
            // NICHT gefangen. Der eigentliche Fehler war, dass Avalonia CanExecute() nur nach
            // einem CanExecuteChanged-Event neu abfragt — ohne das Event blieben die Buttons trotz
            // korrekt gewordenem CanExecute()-Ergebnis dauerhaft im alten (ausgegrauten) Zustand
            // haengen. Deshalb hier explizit das Event abonnieren statt nur CanExecute() zu prüfen.
            var db = new FakeIsoDatabaseService();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            bool installRaised = false, updateRaised = false, copyRaised = false;
            vm.RequestVentoyInstallCommand.CanExecuteChanged += (_, _) => installRaised = true;
            vm.RequestVentoyUpdateCommand.CanExecuteChanged += (_, _) => updateRaised = true;
            vm.CopyToStickCommand.CanExecuteChanged += (_, _) => copyRaised = true;

            vm.SelectedDrive = new LinuxBlockDevice("/dev/sdb", "/mnt/sdb", 16_000_000_000L, "Test Stick", false);

            Assert.True(installRaised);
            Assert.True(updateRaised);
            Assert.True(copyRaised);
        }

        [Fact]
        public void RequestVentoyInstallCommand_SetsPendingConfirmationWithDeviceAndSize()
        {
            var vm = BuildVmWithDrive(new VentoyInstallService());
            vm.RequestVentoyInstallCommand.Execute(null);

            Assert.NotNull(vm.PendingConfirmationMessage);
            Assert.Contains("/dev/sdb", vm.PendingConfirmationMessage);
        }

        [Fact]
        public void CancelVentoyCommand_ClearsPendingConfirmation()
        {
            var vm = BuildVmWithDrive(new VentoyInstallService());
            vm.RequestVentoyInstallCommand.Execute(null);
            vm.CancelVentoyCommand.Execute(null);

            Assert.Null(vm.PendingConfirmationMessage);
        }

        [Fact]
        public async System.Threading.Tasks.Task ConfirmVentoyCommand_Success_SetsDoneStatusAndClearsConfirmation()
        {
            // Fake-Skript VORAB im Cache-Verzeichnis ablegen: EnsureVentoyScriptAsync findet es dort
            // sofort (FindVentoy2DiskSh-Check vor dem Download) und ueberspringt Download/Entpacken
            // komplett — kein echtes Netzwerk noetig, fetchLatestUrl/download bleiben unbenutzt.
            string cacheDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-ventoy-vm-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(cacheDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(cacheDir, "Ventoy2Disk.sh"), "#!/bin/bash\n");

            var ventoyService = new VentoyInstallService(
                runElevated: (cmd, args, onLog, token) => System.Threading.Tasks.Task.FromResult((0, "")),
                cacheDir: cacheDir);
            var vm = BuildVmWithDrive(ventoyService);
            vm.RequestVentoyInstallCommand.Execute(null);

            await vm.ConfirmVentoyAsync();

            Assert.Null(vm.PendingConfirmationMessage);
            System.IO.Directory.Delete(cacheDir, true);
        }
    }
}
