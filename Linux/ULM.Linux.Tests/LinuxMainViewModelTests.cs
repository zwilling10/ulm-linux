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

        // DownloadQueueAsync() nutzt seit der Warteschlange/Parallelität/Pipeline-Phase (2026-09-04)
        // den echten, plattformneutralen DownloadWorker (Core/Workers/Workers.cs) statt eines
        // injizierbaren Test-Delegates — wie beim Windows-Pendant MainViewModel.StartDownload()
        // (siehe ULM.Tests/DownloadWorkerTests.cs, testet dort ebenfalls nur die reine
        // Worker-Logik, nicht den vollen netzwerkbehafteten Ablauf). Seit der Windows-Parität-Phase
        // (2026-09-04, zweiter Durchgang) klärt der Code-behind (MainWindow.axaml.cs
        // BtnDownload_Click, braucht ein Owner-Fenster für die Dialogkette) die Warteschlange VOR
        // dem Aufruf — DownloadQueueAsync bekommt sie fertig übergeben und ist dadurch bewusst kein
        // Ort für die "nichts ausgewählt"-Meldung mehr (die zeigt jetzt InfoDialog im Code-behind).

        [Fact]
        public async System.Threading.Tasks.Task DownloadQueueAsync_EmptyQueue_DoesNothing()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");

            await vm.DownloadQueueAsync(new System.Collections.Generic.List<IsoEntry>(), null, false, false, 1);

            Assert.Equal(string.Empty, vm.DownloadStatus);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public void GetSelectedEntries_ReturnsOnlyCheckedEntries()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            vm.Rows.First(r => r.Name == "Debian 12").IsSelected = true;

            var selected = vm.GetSelectedEntries();

            Assert.Single(selected);
            Assert.Equal("Debian 12", selected[0].Name);
        }

        // Windows-Pendant: MainViewModel.AutoVersionCheckCompleted, treibt dort
        // RunLocalFileMaintenanceAsync() ("Datenmüll-Schutz", Nutzerwunsch 2026-09-04) — hier nur
        // der netzwerkfreie Leerlauf-Zweig testbar (leere DB), der Haupt-Erfolgspfad läuft über
        // AutoVersionCheckWorker mit echten Netzwerkaufrufen, analog zu TriggerAutoVersionCheckAsync
        // insgesamt (kein bestehender Test dafür, siehe restliche Testdatei).
        [Fact]
        public async System.Threading.Tasks.Task TriggerAutoVersionCheckAsync_EmptyDatabase_StillFiresCompletedEvent()
        {
            var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), "/tmp/ulm-linux-vm-test");
            bool fired = false;
            vm.AutoVersionCheckCompleted += () => fired = true;

            await vm.TriggerAutoVersionCheckAsync();

            Assert.True(fired);
        }

        // Windows-Pendant: MainViewModel.DeduplicateEntries() — hier über die beobachtbare
        // Konstruktor-Wirkung getestet (die Methode selbst ist wie unter Windows private).
        // "Gesundheitscheck & Duplikat-Schutz", Nutzerwunsch 2026-09-04.
        [Fact]
        public void Constructor_MergesFuzzyDuplicatesAndKeepsNewerFilename()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu-24.04-desktop-amd64.iso" });
            db.Add(new IsoEntry { Name = "Ubuntu 26.04 LTS", Category = "Einsteiger", Filename = "ubuntu-26.04-desktop-amd64.iso" });

            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");

            Assert.Single(db.Entries);
            Assert.Equal("ubuntu-26.04-desktop-amd64.iso", db.Entries[0].Filename);
        }

        // Windows-Pendant: MainViewModel.AddImportedEntry() — der Kern des "Duplikat-Schutz"-
        // Versprechens aus der Linux-Hilfe (Str.Help_Item_DuplicateProtection_Body): eine ältere,
        // zufällig gefundene Version darf den Katalog nicht rückwärts degradieren.
        [Fact]
        public void AddImportedEntry_OlderVersionMatch_KeepsExistingNewerFilenameAndAddsNoDuplicate()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Ubuntu 26.04 LTS", Category = "Einsteiger", Filename = "ubuntu-26.04-desktop-amd64.iso" });
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");

            vm.AddImportedEntry(new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu-24.04-desktop-amd64.iso" });

            Assert.Single(db.Entries);
            Assert.Equal("ubuntu-26.04-desktop-amd64.iso", db.Entries[0].Filename);
        }

        [Fact]
        public void AddImportedEntry_NoExistingMatch_AddsAsNewEntry()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Fedora Workstation", Category = "Fortgeschrittene", Filename = "fedora-Workstation-Live-44-1.7.x86_64.iso" });
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");

            vm.AddImportedEntry(new IsoEntry { Name = "Debian 12", Category = "Fortgeschrittene", Filename = "debian-12.iso" });

            Assert.Equal(2, db.Entries.Count);
        }

        // Windows-Pendant: MainViewModel.ApplyResolvedUpdatesAndOfferStickUpdate() — Nutzerfund
        // (2026-09-06): "geladen wird die alte, das ist bei allen so". Root Cause: Der Online-Check
        // setzte bisher nur die Laufzeit-Felder RemoteVersion/RemoteUrl/RemoteFilename (Badge "🆕
        // v...."), ohne sie je in die PERSISTIERTEN Filename/Url-Felder zu übernehmen — der Katalog
        // aktualisierte sich dadurch nie wirklich, nur die Anzeige tat so als ob.
        [Fact]
        public void ApplyResolvedUpdates_NewerRemoteVersion_UpdatesFilenameUrlAndRenamesEntry()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry
            {
                Name = "Clonezilla 3.3.0-33", Category = "Rettung",
                Filename = "clonezilla-live-3.3.0-33-amd64.iso",
                RemoteVersion = "3.3.3-15",
                RemoteUrl = "https://master.dl.sourceforge.net/project/clonezilla/clonezilla-live-3.3.3-15-amd64.iso",
                RemoteFilename = "clonezilla-live-3.3.3-15-amd64.iso",
                UpdateAvailable = true
            });
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");

            vm.ApplyResolvedUpdates(new System.Collections.Generic.List<int> { 0 }, false);

            var e = db.Entries[0];
            Assert.Equal("clonezilla-live-3.3.3-15-amd64.iso", e.Filename);
            Assert.Equal("Clonezilla 3.3.3-15", e.Name);
            Assert.False(e.UpdateAvailable);
            Assert.Equal(e.RemoteUrl, e.Url);
        }

        [Fact]
        public void ApplyResolvedUpdates_NoUpdatesButUrlDiscovered_StillSaves()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new IsoEntry { Name = "Fedora Workstation 41", Category = "Fortgeschrittene", Url = "https://example.org/fedora.iso" });
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            int savesBefore = db.SaveCount;

            vm.ApplyResolvedUpdates(new System.Collections.Generic.List<int>(), true);

            Assert.True(db.SaveCount > savesBefore);
        }

        // Windows-Pendant: MainViewModel.RunPipelineCopyConsumerAsync() — Nutzerfund (2026-09-06):
        // "Kopie startet nicht sofort nach abgeschlossenem Download". Testet die reine Kopierschleife
        // isoliert (echte lokale Dateien statt Netzwerk-Downloads) über einen befüllten Channel, wie
        // ihn DownloadQueueAsync normalerweise aus den ItemCompleted-Ereignissen des DownloadWorker
        // speist.
        // Prüft absichtlich NICHT, ob die Quelldatei nach deleteAfter:true verschwunden ist — das
        // Löschen läuft (wie der gesamte übrige Zeilen-Status in dieser Methode) über
        // Dispatcher.UIThread.Post, bewusst NICHT blockierend (siehe Klassenkommentar zum
        // Freeze-Bugfix 2026-09-05) — in einem Test-Host ohne laufende Avalonia-Message-Pump ist
        // der genaue Ausführungszeitpunkt dieses Post() nicht deterministisch abwartbar. Die
        // deleteAfter-Verzweigung selbst wird durch den zweiten Test unten (deleteAfter:false →
        // Datei bleibt garantiert bestehen) abgedeckt.
        [Fact]
        public async System.Threading.Tasks.Task RunPipelineCopyConsumerAsync_CopiesFileToStickAndReportsSuccess()
        {
            string downloadDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-pipeline-src-{Guid.NewGuid():N}");
            string stickDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-pipeline-stick-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(downloadDir);
            System.IO.Directory.CreateDirectory(stickDir);
            try
            {
                string filename = "test-distro.iso";
                string srcPath = System.IO.Path.Combine(downloadDir, filename);
                System.IO.File.WriteAllBytes(srcPath, new byte[1024]);

                var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), downloadDir);
                var entry = new IsoEntry { Name = "Test Distro", Category = "Testen", Filename = filename };

                var channel = System.Threading.Channels.Channel.CreateUnbounded<IsoEntry>();
                channel.Writer.TryWrite(entry);
                channel.Writer.Complete();

                var (ok, failed) = await vm.RunPipelineCopyConsumerAsync(channel.Reader, stickDir, deleteAfter: true, System.Threading.CancellationToken.None);

                Assert.Equal(1, ok);
                Assert.Equal(0, failed);
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(stickDir, "Testen", filename)));
            }
            finally
            {
                try { System.IO.Directory.Delete(downloadDir, true); } catch { }
                try { System.IO.Directory.Delete(stickDir, true); } catch { }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task RunPipelineCopyConsumerAsync_DeleteAfterFalse_KeepsSourceFile()
        {
            string downloadDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-pipeline-src-{Guid.NewGuid():N}");
            string stickDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-pipeline-stick-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(downloadDir);
            System.IO.Directory.CreateDirectory(stickDir);
            try
            {
                string filename = "test-distro.iso";
                string srcPath = System.IO.Path.Combine(downloadDir, filename);
                System.IO.File.WriteAllBytes(srcPath, new byte[1024]);

                var vm = new LinuxMainViewModel(new FakeIsoDatabaseService(), downloadDir);
                var entry = new IsoEntry { Name = "Test Distro", Category = "Testen", Filename = filename };

                var channel = System.Threading.Channels.Channel.CreateUnbounded<IsoEntry>();
                channel.Writer.TryWrite(entry);
                channel.Writer.Complete();

                // Windows-Pendant löscht hier unbedingt (Nutzerfund-Analyse, siehe RunPipelineCopyConsumerAsync-
                // Kommentar) — Linux respektiert deleteAfter=false bewusst korrekt.
                var (ok, _) = await vm.RunPipelineCopyConsumerAsync(channel.Reader, stickDir, deleteAfter: false, System.Threading.CancellationToken.None);

                Assert.Equal(1, ok);
                Assert.True(System.IO.File.Exists(srcPath));
            }
            finally
            {
                try { System.IO.Directory.Delete(downloadDir, true); } catch { }
                try { System.IO.Directory.Delete(stickDir, true); } catch { }
            }
        }

        // Windows-Pendant: keins nötig — UsbService.SafeRecursiveSearch() ist geteilter Core-Code
        // (Core/Services/UsbService.cs), der Bug ist aber NUR auf Linux beobachtbar. Nutzerfund
        // (2026-09-06): "USB-Scan zeigt vorhandene Distros nicht an" — 0 Dateien gefunden trotz
        // real vorhandener ISOs in Kategorie-Ordnern auf dem Stick.
        //
        // Root Cause: SafeRecursiveSearch() überspringt Ordner namens "ventoy" (gedacht für den
        // internen Ventoy-Konfigurationsordner AUF der Partition) per Namensvergleich — OHNE zu
        // unterscheiden, ob das der WURZELORDNER selbst ist. Windows übergibt hier immer einen
        // Laufwerksbuchstaben ("D:\"), dessen Path.GetFileName() leer ist — die Kollision kann
        // dort nie auftreten. Linux übergibt den echten Mountpoint-Pfad, dessen letztes
        // Pfadsegment der DATENTRÄGER-LABEL ist — und Ventoy vergibt der exFAT-Partition per
        // Standard exakt das Label "Ventoy". Path.GetFileName(mountPoint) liefert dann "Ventoy",
        // was per OrdinalIgnoreCase-Vergleich fälschlich als "das ist der ventoy-Unterordner,
        // überspringen" erkannt wird — SafeRecursiveSearch bricht dadurch VOR dem ersten
        // Directory.GetFiles()-Aufruf ab, komplett unabhängig vom tatsächlichen Inhalt des Sticks.
        [Fact]
        public void ScanStick_MountPointNamedVentoy_StillFindsIsosInCategoryFolders()
        {
            // Der Mountpoint selbst muss "Ventoy" heißen (Ventoys Standard-Datenträger-Label),
            // eine eindeutige Guid-Elternebene vermeidet Kollisionen zwischen Testläufen.
            string stickRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-stick-test-{Guid.NewGuid():N}", "Ventoy");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(stickRoot, "ventoy")); // interner Ventoy-Ordner
            string category = System.IO.Path.Combine(stickRoot, "Einsteiger");
            System.IO.Directory.CreateDirectory(category);
            string isoPath = System.IO.Path.Combine(category, "pop-os_24.04_amd64_nvidia_12.iso");
            System.IO.File.WriteAllBytes(isoPath, new byte[1024]);
            try
            {
                var found = ULM.Core.Services.UsbService.Instance.ScanStick(stickRoot, new List<IsoEntry>());

                Assert.Contains(found, f => f.Filename == "pop-os_24.04_amd64_nvidia_12.iso" && f.Category == "Einsteiger");
            }
            finally { try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(stickRoot)!, true); } catch { } }
        }

        // Windows-Pendant: MainViewModel.RefreshAllEntries() — Nutzerfund (2026-09-06): der
        // Datenmüll-Schutz-Dialog rief nach einer Löschung bisher Refresh() auf (voller DB-Reload
        // von der Platte), was die gerade erst ermittelten, nicht persistierten Online-Check-
        // Ergebnisse (RemoteVersion/UpdateAvailable) wegwischte. RefreshRows() muss die Zeilenliste
        // neu aufbauen, OHNE _db.Load() aufzurufen.
        [Fact]
        public void RefreshRows_DoesNotReloadDatabaseFromDisk()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            int loadsBefore = db.LoadCount;

            vm.RefreshRows();

            Assert.Equal(loadsBefore, db.LoadCount);
            Assert.Equal(3, vm.Rows.Count);
        }

        [Fact]
        public void RefreshCommand_StillReloadsDatabaseFromDisk()
        {
            var db = BuildDb();
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test");
            int loadsBefore = db.LoadCount;

            vm.RefreshCommand.Execute(null);

            Assert.Equal(loadsBefore + 1, db.LoadCount);
        }

        [Fact]
        public void CancelDownloadCommand_DisabledWithoutRunningDownload()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            Assert.False(vm.CancelDownloadCommand.CanExecute(null));
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
        public async System.Threading.Tasks.Task VerifyStickIntegrityAsync_NoDriveSelected_IsNoOp()
        {
            var vm = new LinuxMainViewModel(BuildDb(), "/tmp/ulm-linux-vm-test");
            await vm.VerifyStickIntegrityAsync();
            Assert.Equal(string.Empty, vm.IntegrityStatus);
        }

        // Legt ein ISO-großes (>= Constants.MinIsoSizeBytes) Sparse-File an — UsbService.
        // ScanStickVerifiedAsync verwirft ohne Online-URL (unsere Test-Entries haben keine)
        // sonst jede Datei unter dieser Schwelle als "unvollständig", bevor der eigentliche
        // Hash-Vergleich überhaupt zum Zug kommt. SetLength erzeugt das Sparse-File nahezu
        // sofort (kein echtes Beschreiben von 300+ MB).
        private static void CreateIsoSizedFile(string path)
        {
            using var fs = System.IO.File.Create(path);
            fs.SetLength(Constants.MinIsoSizeBytes + 1_000_000);
        }

        [Fact]
        public async System.Threading.Tasks.Task VerifyStickIntegrityAsync_MatchingHash_NoMismatch()
        {
            var db = new FakeIsoDatabaseService();
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" };
            db.Add(entry);
            string stickDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-integrity-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(stickDir);
            string isoPath = System.IO.Path.Combine(stickDir, "ubuntu.iso");
            CreateIsoSizedFile(isoPath);
            entry.Sha256 = await IsoEntry.ComputeSha256Async(isoPath);

            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test")
            {
                SelectedDrive = new LinuxBlockDevice(stickDir, stickDir, 400_000_000L, "Test", true),
            };

            await vm.VerifyStickIntegrityAsync();

            Assert.False(entry.HashMismatchDetected);
            Assert.Equal(string.Format(LocalizationService.T(Str.Log_IsosVerifiedStatus), 1), vm.IntegrityStatus);
            System.IO.Directory.Delete(stickDir, true);
        }

        [Fact]
        public async System.Threading.Tasks.Task VerifyStickIntegrityAsync_MismatchedHash_SetsHashMismatchDetected()
        {
            var db = new FakeIsoDatabaseService();
            var entry = new IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso", Sha256 = new string('a', 64) };
            db.Add(entry);
            string stickDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-integrity-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(stickDir);
            CreateIsoSizedFile(System.IO.Path.Combine(stickDir, "ubuntu.iso"));

            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test")
            {
                SelectedDrive = new LinuxBlockDevice(stickDir, stickDir, 400_000_000L, "Test", true),
            };

            await vm.VerifyStickIntegrityAsync();

            Assert.True(entry.HashMismatchDetected);
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
                cacheDir: cacheDir,
                // Kein echter udisksctl-Aufruf in Tests — SelectedDrive.DeviceNode ist hier
                // "/dev/sdb", auf dem Testrechner potenziell ein echtes, eingehängtes Gerät.
                unmount: (dev, log, ct) => System.Threading.Tasks.Task.CompletedTask);
            var vm = BuildVmWithDrive(ventoyService);
            vm.RequestVentoyInstallCommand.Execute(null);

            await vm.ConfirmVentoyAsync();

            Assert.Null(vm.PendingConfirmationMessage);
            System.IO.Directory.Delete(cacheDir, true);
        }
    }
}
