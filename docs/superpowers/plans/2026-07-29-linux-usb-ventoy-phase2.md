# ULM Linux GUI Phase 2 (USB-Erkennung + Ventoy) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ULM Linux erkennt USB-Wechseldatenträger über `lsblk`, kann ISOs auf einen bestehenden Ventoy-Stick kopieren und einen frischen/bestehenden Stick per `pkexec` + `Ventoy2Disk.sh` neu einrichten/aktualisieren.

**Architecture:** Zwei neue, unabhängig testbare Services (`LinuxUsbService` für `lsblk`-Enumeration, `VentoyInstallService` für Download+`pkexec`-Aufruf), beide mit injizierbaren Prozess-Runner-Delegates. Erweitert das bestehende `LinuxMainViewModel` um Geräte-Liste, Kopier- und Ventoy-Install-Commands. Bestehender, bereits portabler Code aus `Core/Services/UsbService.cs` (`IsVentoyInstalled`, `UpdateVentoyMenu`, `EnsureVentoyTheme`, `DriveFreeMb`) wird unverändert per `<Compile Include>` wiederverwendet.

**Tech Stack:** .NET 8, `System.Formats.Tar` (Teil des .NET-8-SDK, kein neues NuGet-Paket) für `.tar.gz`-Entpacken, `System.Text.Json` für `lsblk -J`-Parsing.

## Global Constraints

- Kein Windows-Code (`UniversalLinuxManager.csproj`, `Views/*`, `ViewModels/*` außer `ViewModelBase.cs`) wird verändert.
- Bestehende Testsuiten (209 Windows, 22 Linux nach Phase 1) müssen nach jedem Task weiterhin grün sein.
- Keine automatische Geräteauswahl — jede destruktive Aktion zeigt Gerätepfad + Größe vor Bestätigung.
- Kein neues NuGet-Paket, kein separates Dialog-Fenster für die Bestätigung (inline im Hauptfenster).
- `pkexec` für Elevation, kein Neustart von ULM selbst als erhöhter Prozess.
- Spec-Referenz: `docs/superpowers/specs/2026-07-29-linux-usb-ventoy-design.md`.
- **Claude hat kein Linux-System und keinen USB-Stick zum Testen** — jeder Task endet mit `dotnet build`/`dotnet test`, die eigentliche Hardware-Verifikation ist Aufgabe des Nutzers (Task 9).

---

## Datei-Übersicht

**Neu:**
- `Linux/LinuxUsbService.cs` — `LinuxBlockDevice`-Record, `lsblk -J`-Parsing, Geräte-Enumeration.
- `Linux/VentoyInstallService.cs` — Ventoy-Linux-Release-Auflösung, Download, Entpacken, `pkexec`-Aufruf.
- `Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs`
- `Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs`

**Geändert:**
- `Linux/ULM.Linux.csproj` — `Core/Services/UsbService.cs` zur Compile-Include-Liste ergänzt.
- `Linux/ViewModels/LinuxMainViewModel.cs` — Drives-Collection, Kopier-Command, Ventoy-Install/Update-Commands, Bestätigungs-Zustand.
- `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs` — Tests für die neuen Commands.
- `Linux/Views/MainWindow.axaml` — Geräte-Liste, Secure-Boot-Checkbox, Inline-Bestätigungsleiste.
- `Linux/App.axaml.cs` — `DispatcherTimer`-Polling (8s) für Geräte-Aktualisierung.

---

### Task 1: `LinuxBlockDevice` + `lsblk -J`-Parsing (reine Logik, TDD)

**Files:**
- Create: `Linux/LinuxUsbService.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs`

**Interfaces:**
- Produces: `ULM.Linux.LinuxBlockDevice` (`record`, Properties `DeviceNode`, `MountPoint` (`string?`), `SizeBytes` (`long`), `Model` (`string`), `IsVentoyInstalled` (`bool`)); `LinuxUsbService.ParseLsblkJson(string json)` (`internal static List<LinuxBlockDevice>`). Genutzt von Task 2 (echter `lsblk`-Aufruf) und Task 3 (ViewModel-Polling).

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs`:

```csharp
using System.Linq;
using ULM.Linux;
using Xunit;

namespace ULM.Linux.Tests
{
    public class LinuxUsbServiceParseLsblkJsonTests
    {
        private const string SampleJson = """
        {
           "blockdevices": [
              {"name":"sda","size":256060514304,"model":"Samsung SSD 970","rm":false,"type":"disk","mountpoint":null,
               "children": [
                  {"name":"sda1","size":536870912,"model":null,"rm":false,"type":"part","mountpoint":"/boot/efi"},
                  {"name":"sda2","size":255521931264,"model":null,"rm":false,"type":"part","mountpoint":"/"}
               ]
              },
              {"name":"sdb","size":16008609792,"model":"Kingston DataTraveler","rm":true,"type":"disk","mountpoint":null,
               "children": [
                  {"name":"sdb1","size":8388608,"model":null,"rm":true,"type":"part","mountpoint":null},
                  {"name":"sdb2","size":15998607360,"model":null,"rm":true,"type":"part","mountpoint":"/media/max/VENTOY"}
               ]
              },
              {"name":"sdc","size":4000000000,"model":"Generic Flash Disk","rm":true,"type":"disk","mountpoint":null}
           ]
        }
        """;

        [Fact]
        public void ParseLsblkJson_IgnoresNonRemovableDisks()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            Assert.DoesNotContain(devices, d => d.DeviceNode == "/dev/sda");
        }

        [Fact]
        public void ParseLsblkJson_ReturnsRemovableDisksWithDevPrefix()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            Assert.Contains(devices, d => d.DeviceNode == "/dev/sdb");
            Assert.Contains(devices, d => d.DeviceNode == "/dev/sdc");
        }

        [Fact]
        public void ParseLsblkJson_PicksMountPointOfLargestChildPartition()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdb = devices.Single(d => d.DeviceNode == "/dev/sdb");
            Assert.Equal("/media/max/VENTOY", sdb.MountPoint);
        }

        [Fact]
        public void ParseLsblkJson_DeviceWithoutChildren_HasNullMountPoint()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdc = devices.Single(d => d.DeviceNode == "/dev/sdc");
            Assert.Null(sdc.MountPoint);
        }

        [Fact]
        public void ParseLsblkJson_ReadsSizeAndModel()
        {
            var devices = LinuxUsbService.ParseLsblkJson(SampleJson);
            var sdb = devices.Single(d => d.DeviceNode == "/dev/sdb");
            Assert.Equal(16008609792L, sdb.SizeBytes);
            Assert.Equal("Kingston DataTraveler", sdb.Model);
        }
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxUsbServiceParseLsblkJsonTests`
Expected: FAIL — `CS0246: The type or namespace name 'LinuxUsbService' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/LinuxUsbService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ULM.Linux
{
    /// <summary>Ein per lsblk erkannter Wechseldatenträger.</summary>
    public sealed record LinuxBlockDevice(
        string DeviceNode,
        string? MountPoint,
        long SizeBytes,
        string Model,
        bool IsVentoyInstalled);

    public sealed class LinuxUsbService
    {
        public delegate Task<string> RunLsblkFunc();

        private readonly RunLsblkFunc _runLsblk;

        public LinuxUsbService(RunLsblkFunc? runLsblk = null)
        {
            _runLsblk = runLsblk ?? DefaultRunLsblk;
        }

        public async Task<List<LinuxBlockDevice>> ListRemovableDevicesAsync()
        {
            string json = await _runLsblk().ConfigureAwait(false);
            return ParseLsblkJson(json);
        }

        /// <summary>
        /// Reine, testbare Parse-Funktion — kein Prozessaufruf. lsblk-JSON-Feldtypen (rm/size)
        /// variieren je nach util-linux-Version zwischen echten JSON-Zahlen/Booleans und Strings —
        /// GetRawText()+Parsing deckt beide Fälle ab, statt sich auf einen festen Typ zu verlassen.
        /// </summary>
        internal static List<LinuxBlockDevice> ParseLsblkJson(string json)
        {
            var result = new List<LinuxBlockDevice>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var devices)) return result;

            foreach (var dev in devices.EnumerateArray())
            {
                string type = GetString(dev, "type") ?? string.Empty;
                bool removable = GetBool(dev, "rm");
                if (type != "disk" || !removable) continue;

                string name = GetString(dev, "name") ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                string? mountPoint = GetString(dev, "mountpoint");
                if (mountPoint is null && dev.TryGetProperty("children", out var children))
                {
                    long bestSize = -1;
                    foreach (var child in children.EnumerateArray())
                    {
                        string? childMount = GetString(child, "mountpoint");
                        if (childMount is null) continue;
                        long childSize = GetLong(child, "size");
                        if (childSize > bestSize) { bestSize = childSize; mountPoint = childMount; }
                    }
                }

                result.Add(new LinuxBlockDevice(
                    DeviceNode: "/dev/" + name,
                    MountPoint: mountPoint,
                    SizeBytes: GetLong(dev, "size"),
                    Model: GetString(dev, "model") ?? string.Empty,
                    IsVentoyInstalled: mountPoint is not null && UsbService.IsVentoyInstalled(mountPoint)));
            }
            return result;
        }

        private static string? GetString(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

        private static bool GetBool(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return false;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => v.GetString() is "1" or "true",
                JsonValueKind.Number => v.GetInt32() != 0,
                _ => false,
            };
        }

        private static long GetLong(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return 0L;
            if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
            return long.TryParse(v.GetString(), out long n) ? n : 0L;
        }

        private static async Task<string> DefaultRunLsblk()
        {
            var psi = new ProcessStartInfo("lsblk", "-J -b -o NAME,SIZE,MODEL,RM,TYPE,MOUNTPOINT")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null) return "{}";
            string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            proc.WaitForExit(5000);
            return output;
        }
    }
}
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxUsbServiceParseLsblkJsonTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: `Core/Services/UsbService.cs` in die Compile-Liste aufnehmen**

In `Linux/ULM.Linux.csproj`, im `<ItemGroup>` mit den `<Compile Include>`-Einträgen, nach der Zeile für `IsoDatabaseService.cs` ergänzen:

```xml
    <Compile Include="..\Core\Services\UsbService.cs" Link="Core\Services\UsbService.cs" />
```

`ParseLsblkJson` ruft `UsbService.IsVentoyInstalled(mountPoint)` auf — ohne diesen Include-Eintrag schlägt der Build fehl.

- [ ] **Step 6: Build verifizieren**

Run: `dotnet build Linux/ULM.Linux.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add Linux/LinuxUsbService.cs Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs Linux/ULM.Linux.csproj
git commit -m "feat(linux): lsblk-basierte Geraete-Erkennung (LinuxBlockDevice, ParseLsblkJson)"
```

---

### Task 2: `ListRemovableDevicesAsync` mit injiziertem Runner testen

**Files:**
- Test: `Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs`

**Interfaces:**
- Consumes: `LinuxUsbService(RunLsblkFunc? runLsblk = null)` (Task 1).
- Verifiziert nur die Verdrahtung des injizierten Delegates — `ParseLsblkJson` selbst ist bereits in Task 1 getestet.

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs` neue Klasse ergänzen:

```csharp
    public class LinuxUsbServiceListRemovableDevicesTests
    {
        [Fact]
        public async System.Threading.Tasks.Task ListRemovableDevicesAsync_UsesInjectedRunner()
        {
            const string json = """{"blockdevices":[{"name":"sdb","size":16008609792,"model":"Test","rm":true,"type":"disk","mountpoint":null}]}""";
            var service = new LinuxUsbService(() => System.Threading.Tasks.Task.FromResult(json));

            var devices = await service.ListRemovableDevicesAsync();

            Assert.Single(devices);
            Assert.Equal("/dev/sdb", devices[0].DeviceNode);
        }
    }
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxUsbServiceListRemovableDevicesTests`
Expected: FAIL — schlägt fehl, falls `ListRemovableDevicesAsync` den Delegate nicht aufruft (bei korrekter Task-1-Implementierung sollte dieser Test allerdings direkt PASS liefern, da die Methode bereits existiert; dieser Schritt dokumentiert die Erwartung explizit für spätere Refactorings)

- [ ] **Step 3: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxUsbServiceListRemovableDevicesTests`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 4: Volle Linux-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests`
Expected: alle Tests grün (22 aus Phase 1 + 6 neue aus Task 1/2 = 28)

- [ ] **Step 5: Commit**

```bash
git add Linux/ULM.Linux.Tests/LinuxUsbServiceTests.cs
git commit -m "test(linux): Verdrahtung von ListRemovableDevicesAsync mit injiziertem Runner"
```

---

### Task 3: Geräte-Polling in `LinuxMainViewModel`

**Files:**
- Modify: `Linux/ViewModels/LinuxMainViewModel.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Interfaces:**
- Consumes: `LinuxUsbService.ListRemovableDevicesAsync()` (Task 1/2).
- Produces: `LinuxMainViewModel(..., LinuxUsbService? usbService = null)` (neuer optionaler Konstruktor-Parameter, ans Ende der bestehenden Parameterliste angehängt — bestehende Aufrufe bleiben gültig), `Drives` (`ObservableCollection<LinuxBlockDevice>`), `SelectedDrive` (`LinuxBlockDevice?`), `PollDrivesAsync()` (`Task`, öffentlich aufrufbar für Tests UND für das spätere `DispatcherTimer`-Polling in Task 8).

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`, neue Testklasse am Ende der Datei:

```csharp
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
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelDrivesTests`
Expected: FAIL — `CS1739`/`CS0117` (`usbService`-Parameter, `Drives`, `SelectedDrive`, `PollDrivesAsync` existieren noch nicht)

- [ ] **Step 3: Implementierung**

In `Linux/ViewModels/LinuxMainViewModel.cs`, `using`-Liste ergänzen:

```csharp
using System.Collections.Generic;
```

(bereits vorhanden aus Phase 1 — nur zur Vollständigkeit geprüft, kein neuer Eintrag nötig, falls schon da).

Konstruktor-Signatur um den neuen Parameter erweitern (ans Ende anhängen):

```csharp
        private readonly LinuxUsbService _usbService;

        public LinuxMainViewModel(
            IIsoDatabaseService db, string downloadDirectory,
            string? settingsIniPath = null, DownloadFunc? download = null, ResolveFunc? resolve = null,
            LinuxUsbService? usbService = null)
        {
            _db = db;
            _downloadDirectory = downloadDirectory;
            _settingsIniPath = settingsIniPath ?? LinuxPaths.SettingsIni;
            _download = download ?? ((url, dest, progress, token) => HttpService.Instance.DownloadAsync(url, dest, progress, token));
            _resolve = resolve ?? (entry => HttpService.Instance.ResolveLatestAsync(entry));
            _usbService = usbService ?? new LinuxUsbService();

            Categories = new ObservableCollection<CategoryOption>();
            Rows = new ObservableCollection<LinuxIsoRow>();
            Drives = new ObservableCollection<LinuxBlockDevice>();
            RebuildCategories();

            RefreshCommand        = new RelayCommand(Refresh);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            DownloadCommand        = new RelayCommand(() => _ = DownloadSelectedAsync(), () => SelectedRow is not null && !IsBusy);

            ApplyFilter();
        }
```

Neue Properties direkt nach `SelectedRow` einfügen:

```csharp
        public ObservableCollection<LinuxBlockDevice> Drives { get; }

        private LinuxBlockDevice? _selectedDrive;
        public LinuxBlockDevice? SelectedDrive
        {
            get => _selectedDrive;
            set => SetField(ref _selectedDrive, value);
        }
```

Neue Methode am Ende der Klasse (vor der schließenden `}`) einfügen:

```csharp
        public async Task PollDrivesAsync()
        {
            var current = await _usbService.ListRemovableDevicesAsync().ConfigureAwait(true);
            string? selectedNode = SelectedDrive?.DeviceNode;

            Drives.Clear();
            foreach (var d in current) Drives.Add(d);

            SelectedDrive = selectedNode is null
                ? null
                : Drives.FirstOrDefault(d => d.DeviceNode == selectedNode);
        }
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelDrivesTests`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 5: Volle Linux-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests`
Expected: alle Tests grün (28 + 2 = 30)

- [ ] **Step 6: Commit**

```bash
git add Linux/ViewModels/LinuxMainViewModel.cs Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs
git commit -m "feat(linux): Geraete-Polling in LinuxMainViewModel (Drives, SelectedDrive)"
```

---

### Task 4: ISOs auf bestehenden Ventoy-Stick kopieren

**Files:**
- Modify: `Linux/ViewModels/LinuxMainViewModel.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Interfaces:**
- Consumes: `UsbService.DriveFreeMb(string)` (bestehend, `Core/Services/UsbService.cs`), `UsbService.UpdateVentoyMenu(string, IReadOnlyList<IsoEntry>)` (bestehend).
- Produces: `LinuxMainViewModel.CopyToStickCommand` (`RelayCommand`), `CopySelectedToStickAsync()` (`Task`, testbar), `CopyStatus` (`string`).

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`, in `LinuxMainViewModelDrivesTests` ergänzen (oder eigene Klasse — hier direkt angehängt):

```csharp
        [Fact]
        public async System.Threading.Tasks.Task CopySelectedToStickAsync_NoDriveSelected_SetsNoStickStatus()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new ULM.Core.Models.IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" });
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ulm-linux-copy-{System.Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(tempDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(tempDir, "ubuntu.iso"), new byte[1024]);
            var vm = new LinuxMainViewModel(db, tempDir);
            vm.SelectedRow = vm.Rows.First();

            await vm.CopySelectedToStickAsync();

            Assert.Equal(ULM.Infrastructure.LocalizationService.T(ULM.Infrastructure.Str.Linux_Copy_NoDrive), vm.CopyStatus);
            System.IO.Directory.Delete(tempDir, true);
        }

        [Fact]
        public async System.Threading.Tasks.Task CopySelectedToStickAsync_CopiesFileToCategoryFolder()
        {
            var db = new FakeIsoDatabaseService();
            db.Add(new ULM.Core.Models.IsoEntry { Name = "Ubuntu 24.04 LTS", Category = "Einsteiger", Filename = "ubuntu.iso" });
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
```

Hinweis: `LinuxBlockDevice` wird hier mit `DeviceNode = stickDir` (statt einem echten `/dev/sdX`-Pfad) konstruiert — für den Kopier-Test zählt nur `MountPoint`, das bewusst gleich `stickDir` gesetzt ist, damit `DriveInfo(MountPoint)` im echten Dateisystem funktioniert.

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter CopySelectedToStickAsync`
Expected: FAIL — `CS1061`/`CS0117` (`Str.Linux_Copy_NoDrive`, `CopySelectedToStickAsync`, `CopyStatus` existieren noch nicht)

- [ ] **Step 3: Neue Str-Werte ergänzen**

In `Infrastructure/Str.cs`, nach den `Linux_Download_*`-Einträgen ergänzen:

```csharp
        Linux_Copy_NoDrive,
        Linux_Copy_Done,
        Linux_Copy_Failed,
```

In `Infrastructure/LocalizationService.cs`, im `De`-Dictionary nach den `Linux_Download_*`-Einträgen:

```csharp
            [Str.Linux_Copy_NoDrive]               = "Kein USB-Stick ausgewählt.",
            [Str.Linux_Copy_Done]                  = "Kopiert.",
            [Str.Linux_Copy_Failed]                = "Kopieren fehlgeschlagen.",
```

Im `En`-Dictionary an der entsprechenden Stelle:

```csharp
            [Str.Linux_Copy_NoDrive]               = "No USB stick selected.",
            [Str.Linux_Copy_Done]                  = "Copied.",
            [Str.Linux_Copy_Failed]                = "Copy failed.",
```

- [ ] **Step 4: `LinuxMainViewModel` implementieren**

Neue Properties nach `DownloadStatus` einfügen:

```csharp
        private string _copyStatus = string.Empty;
        public string CopyStatus
        {
            get => _copyStatus;
            private set => SetField(ref _copyStatus, value);
        }
```

`RelayCommand`-Feld nach `DownloadCommand` ergänzen und im Konstruktor initialisieren:

```csharp
        public RelayCommand CopyToStickCommand { get; }
```

Im Konstruktor, nach der `DownloadCommand`-Zeile:

```csharp
            CopyToStickCommand = new RelayCommand(() => _ = CopySelectedToStickAsync(), () => SelectedRow is not null && SelectedDrive is not null && !IsBusy);
```

Neue Methode am Ende der Klasse:

```csharp
        public async Task CopySelectedToStickAsync()
        {
            if (SelectedRow is null || IsBusy) return;
            if (SelectedDrive?.MountPoint is null)
            {
                CopyStatus = LocalizationService.T(Str.Linux_Copy_NoDrive);
                return;
            }

            IsoEntry entry = SelectedRow.Entry;
            string mountPoint = SelectedDrive.MountPoint;

            IsBusy = true;
            try
            {
                string srcPath = System.IO.Path.Combine(_downloadDirectory, entry.Filename);
                if (!System.IO.File.Exists(srcPath))
                {
                    CopyStatus = LocalizationService.T(Str.Linux_Copy_Failed);
                    return;
                }

                long fileSize = new System.IO.FileInfo(srcPath).Length;
                try
                {
                    var drive = new System.IO.DriveInfo(mountPoint);
                    if (drive.IsReady && drive.AvailableFreeSpace < fileSize)
                    {
                        CopyStatus = LocalizationService.T(Str.Linux_Copy_Failed);
                        return;
                    }
                }
                catch { /* Freispeicher-Check ist best-effort, analog zu CopyToUsbWorker */ }

                string destDir  = System.IO.Path.Combine(mountPoint, entry.Category);
                System.IO.Directory.CreateDirectory(destDir);
                string destPath = System.IO.Path.Combine(destDir, entry.Filename);
                await Task.Run(() => System.IO.File.Copy(srcPath, destPath, overwrite: true)).ConfigureAwait(true);

                UsbService.UpdateVentoyMenu(mountPoint, _db.Entries);
                CopyStatus = LocalizationService.T(Str.Linux_Copy_Done);
            }
            catch
            {
                CopyStatus = LocalizationService.T(Str.Linux_Copy_Failed);
            }
            finally
            {
                IsBusy = false;
            }
        }
```

`using ULM.Core.Services;` ist bereits vorhanden (aus Phase 1, für `HttpService`/`IIsoDatabaseService`) — `UsbService` liegt im selben Namespace, kein neuer `using`-Eintrag nötig.

- [ ] **Step 5: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter CopySelectedToStickAsync`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Volle Linux- und Windows-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet test ULM.Tests`
Expected: beide `Passed! - Failed: 0`

- [ ] **Step 7: Commit**

```bash
git add Linux/ViewModels/LinuxMainViewModel.cs Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs Infrastructure/Str.cs Infrastructure/LocalizationService.cs
git commit -m "feat(linux): ISOs auf bestehenden Ventoy-Stick kopieren (CopyToStickCommand)"
```

---

### Task 5: `VentoyInstallService` — Release-Auflösung + Entpacken

**Files:**
- Create: `Linux/VentoyInstallService.cs`
- Test: `Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs`

**Interfaces:**
- Produces: `VentoyInstallService.FetchLatestVentoyUrlFunc` (`delegate Task<string> FetchLatestVentoyUrlFunc()`), `VentoyInstallService.DownloadFunc` (wiederverwendet dasselbe Signatur-Muster wie `LinuxMainViewModel.DownloadFunc`, aber eigenständig definiert, da `VentoyInstallService` nicht von `LinuxMainViewModel` abhängen soll), `EnsureVentoyScriptAsync(Action<string>? onLog, CancellationToken token)` (`internal Task<string>`, gibt den Pfad zu `Ventoy2Disk.sh` nach Download+Entpacken zurück, oder `string.Empty` bei Fehler).

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs`:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using ULM.Linux;
using Xunit;

namespace ULM.Linux.Tests
{
    public class VentoyInstallServiceEnsureScriptTests
    {
        private static byte[] BuildFakeVentoyTarGz(string scriptContent)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-fixture-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempDir, "ventoy-1.0.99"));
            string scriptPath = Path.Combine(tempDir, "ventoy-1.0.99", "Ventoy2Disk.sh");
            File.WriteAllText(scriptPath, scriptContent);

            string tarPath = Path.Combine(tempDir, "ventoy.tar");
            System.Formats.Tar.TarFile.CreateFromDirectory(Path.Combine(tempDir, "ventoy-1.0.99"), tarPath, includeBaseDirectory: true);

            string gzPath = tarPath + ".gz";
            using (var tarStream = File.OpenRead(tarPath))
            using (var gzStream = File.Create(gzPath))
            using (var gzip = new GZipStream(gzStream, CompressionMode.Compress))
                tarStream.CopyTo(gzip);

            byte[] bytes = File.ReadAllBytes(gzPath);
            Directory.Delete(tempDir, true);
            return bytes;
        }

        [Fact]
        public async Task EnsureVentoyScriptAsync_DownloadsAndExtractsScript()
        {
            byte[] tarGzBytes = BuildFakeVentoyTarGz("#!/bin/bash\necho fake ventoy script\n");
            string cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");

            var service = new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy-1.0.99-linux.tar.gz"),
                download: async (url, destPath, progress, token) =>
                {
                    await File.WriteAllBytesAsync(destPath, tarGzBytes, token);
                    return true;
                },
                cacheDir: cacheDir);

            string scriptPath = await service.EnsureVentoyScriptAsync(onLog: null, CancellationToken.None);

            Assert.True(File.Exists(scriptPath));
            Assert.Equal("Ventoy2Disk.sh", Path.GetFileName(scriptPath));
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task EnsureVentoyScriptAsync_DownloadFails_ReturnsEmpty()
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");
            var service = new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy-1.0.99-linux.tar.gz"),
                download: (url, destPath, progress, token) => Task.FromResult(false),
                cacheDir: cacheDir);

            string scriptPath = await service.EnsureVentoyScriptAsync(onLog: null, CancellationToken.None);

            Assert.Equal(string.Empty, scriptPath);
        }
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter VentoyInstallServiceEnsureScriptTests`
Expected: FAIL — `CS0246: The type or namespace name 'VentoyInstallService' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/VentoyInstallService.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ULM.Linux
{
    public sealed class VentoyInstallService
    {
        public delegate Task<string> FetchLatestUrlFunc();
        public delegate Task<bool> DownloadFunc(string url, string destPath, IProgress<(int Percent, string Detail)>? progress, CancellationToken token);
        public delegate Task<(int ExitCode, string Output)> RunElevatedFunc(string command, string args, Action<string>? onOutputLine, CancellationToken token);

        private readonly FetchLatestUrlFunc _fetchLatestUrl;
        private readonly DownloadFunc _download;
        private readonly RunElevatedFunc _runElevated;
        private readonly string _cacheDir;

        public VentoyInstallService(
            FetchLatestUrlFunc? fetchLatestUrl = null, DownloadFunc? download = null,
            RunElevatedFunc? runElevated = null, string? cacheDir = null)
        {
            _fetchLatestUrl = fetchLatestUrl ?? DefaultFetchLatestUrlAsync;
            _download = download ?? DefaultDownloadAsync;
            _runElevated = runElevated ?? DefaultRunElevatedAsync;
            _cacheDir = cacheDir ?? Path.Combine(LinuxPaths.DataDir, "ventoy-cache");
        }

        /// <summary>
        /// Lädt (falls noch nicht im Cache vorhanden) das offizielle Ventoy-Linux-Release von
        /// GitHub und entpackt es. Gibt den Pfad zu Ventoy2Disk.sh zurück, oder string.Empty bei
        /// Fehler. Analog zu VentoyInstallWorker.FetchLatestVentoyUrlAsync/FindVentoy2DiskExe in
        /// Core/Workers/Workers.cs, nur für das Linux-Release (.tar.gz statt .zip, .sh statt .exe).
        /// </summary>
        public async Task<string> EnsureVentoyScriptAsync(Action<string>? onLog, CancellationToken token)
        {
            string existing = FindVentoy2DiskSh(_cacheDir);
            if (!string.IsNullOrEmpty(existing)) return existing;

            Directory.CreateDirectory(_cacheDir);
            string url = await _fetchLatestUrl().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(url)) { onLog?.Invoke("Ventoy-URL konnte nicht ermittelt werden."); return string.Empty; }

            string archivePath = Path.Combine(_cacheDir, "ventoy_latest.tar.gz");
            bool ok = await _download(url, archivePath, null, token).ConfigureAwait(false);
            if (!ok) { onLog?.Invoke("Ventoy-Download fehlgeschlagen."); return string.Empty; }

            string extractDir = Path.Combine(_cacheDir, "extracted");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            using (var fileStream = File.OpenRead(archivePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzipStream, extractDir, overwriteFiles: true);

            return FindVentoy2DiskSh(extractDir);
        }

        private static string FindVentoy2DiskSh(string dir)
        {
            if (!Directory.Exists(dir)) return string.Empty;
            return Directory.GetFiles(dir, "Ventoy2Disk.sh", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        }

        private static async Task<string> DefaultFetchLatestUrlAsync()
        {
            const string Fallback = "https://github.com/ventoy/Ventoy/releases/download/v1.0.99/ventoy-1.0.99-linux.tar.gz";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ventoy/Ventoy/releases/latest");
                req.Headers.UserAgent.ParseAdd("ULM-Linux/1.0");
                using var resp = await client.SendAsync(req).ConfigureAwait(false);
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                {
                    string n = a.GetProperty("name").GetString() ?? "";
                    string u = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (!string.IsNullOrEmpty(u) && n.Contains("linux", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                        return u;
                }
            }
            catch { }
            return Fallback;
        }

        private static async Task<bool> DefaultDownloadAsync(string url, string destPath, IProgress<(int, string)>? progress, CancellationToken token)
            => await ULM.Core.Services.HttpService.Instance.DownloadAsync(url, destPath, progress, token).ConfigureAwait(false);

        private static Task<(int, string)> DefaultRunElevatedAsync(string command, string args, Action<string>? onOutputLine, CancellationToken token)
            => Task.FromResult((-1, string.Empty)); // wird in Task 6 ersetzt
    }
}
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter VentoyInstallServiceEnsureScriptTests`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 5: Build verifizieren**

Run: `dotnet build Linux/ULM.Linux.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add Linux/VentoyInstallService.cs Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs
git commit -m "feat(linux): VentoyInstallService - Release-Aufloesung und tar.gz-Entpacken"
```

---

### Task 6: `VentoyInstallService.InstallOrUpdateAsync` — `pkexec`-Aufruf

**Files:**
- Modify: `Linux/VentoyInstallService.cs`
- Test: `Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs`

**Interfaces:**
- Produces: `VentoyInstallService.InstallOrUpdateAsync(string deviceNode, bool updateMode, bool secureBoot, Action<string>? onLog, CancellationToken token)` (`Task<bool>`). Genutzt von Task 7 (`LinuxMainViewModel`).

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs` neue Klasse ergänzen:

```csharp
    public class VentoyInstallServiceInstallOrUpdateTests
    {
        private static VentoyInstallService BuildServiceWithFakeScript(
            VentoyInstallService.RunElevatedFunc runElevated, out string cacheDir)
        {
            cacheDir = Path.Combine(Path.GetTempPath(), $"ulm-ventoy-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, "Ventoy2Disk.sh"), "#!/bin/bash\n");
            return new VentoyInstallService(
                fetchLatestUrl: () => Task.FromResult("https://example.invalid/ventoy.tar.gz"),
                download: (u, d, p, t) => Task.FromResult(true),
                runElevated: runElevated,
                cacheDir: cacheDir);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_Install_PassesInstallFlagAndDevice()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            bool ok = await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.True(ok);
            Assert.Contains("-i", capturedArgs);
            Assert.Contains("/dev/sdb", capturedArgs);
            Assert.DoesNotContain("-s", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_InstallWithSecureBoot_PassesSecureBootFlag()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: true, onLog: null, CancellationToken.None);

            Assert.Contains("-s", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_UpdateMode_PassesUpdateFlagNotInstall()
        {
            string? capturedArgs = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedArgs = args; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: true, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.Contains("-u", capturedArgs);
            Assert.DoesNotContain("-i", capturedArgs);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_NonZeroExitCode_ReturnsFalse()
        {
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => Task.FromResult((1, "error")),
                out string cacheDir);

            bool ok = await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.False(ok);
            Directory.Delete(cacheDir, true);
        }

        [Fact]
        public async Task InstallOrUpdateAsync_UsesPkexecAsCommand()
        {
            string? capturedCommand = null;
            var service = BuildServiceWithFakeScript(
                (cmd, args, onLog, token) => { capturedCommand = cmd; return Task.FromResult((0, "")); },
                out string cacheDir);

            await service.InstallOrUpdateAsync("/dev/sdb", updateMode: false, secureBoot: false, onLog: null, CancellationToken.None);

            Assert.Equal("pkexec", capturedCommand);
            Directory.Delete(cacheDir, true);
        }
    }
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter VentoyInstallServiceInstallOrUpdateTests`
Expected: FAIL — `CS1061: 'VentoyInstallService' does not contain a definition for 'InstallOrUpdateAsync'`

- [ ] **Step 3: Implementierung**

In `Linux/VentoyInstallService.cs`, die Platzhalter-Methode `DefaultRunElevatedAsync` ersetzen und `InstallOrUpdateAsync` ergänzen:

```csharp
        public async Task<bool> InstallOrUpdateAsync(
            string deviceNode, bool updateMode, bool secureBoot, Action<string>? onLog, CancellationToken token)
        {
            string scriptPath = await EnsureVentoyScriptAsync(onLog, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(scriptPath))
            {
                onLog?.Invoke("Ventoy2Disk.sh konnte nicht bereitgestellt werden.");
                return false;
            }

            string flag = updateMode ? "-u" : "-i";
            string args = secureBoot && !updateMode
                ? $"\"{scriptPath}\" {flag} -s {deviceNode}"
                : $"\"{scriptPath}\" {flag} {deviceNode}";

            var (exitCode, output) = await _runElevated("pkexec", $"bash {args}", onLog, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(output)) onLog?.Invoke(output);
            return exitCode == 0;
        }

        private static async Task<(int, string)> DefaultRunElevatedAsync(string command, string args, Action<string>? onOutputLine, CancellationToken token)
        {
            var psi = new ProcessStartInfo(command, args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null) return (-1, string.Empty);

            var stdoutBuilder = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdoutBuilder.AppendLine(e.Data); onOutputLine?.Invoke(e.Data); } };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) { stdoutBuilder.AppendLine(e.Data); onOutputLine?.Invoke(e.Data); } };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(token).ConfigureAwait(false);
            return (proc.ExitCode, stdoutBuilder.ToString());
        }
```

Die alte Zeile

```csharp
        private static Task<(int, string)> DefaultRunElevatedAsync(string command, string args, Action<string>? onOutputLine, CancellationToken token)
            => Task.FromResult((-1, string.Empty)); // wird in Task 6 ersetzt
```

vollständig durch die obige neue Implementierung ersetzen (nicht beide stehen lassen — doppelte Methode).

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter VentoyInstallServiceInstallOrUpdateTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Volle Linux-Testsuite + Build prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet build Linux/ULM.Linux.csproj`
Expected: beide grün/fehlerfrei

- [ ] **Step 6: Commit**

```bash
git add Linux/VentoyInstallService.cs Linux/ULM.Linux.Tests/VentoyInstallServiceTests.cs
git commit -m "feat(linux): VentoyInstallService.InstallOrUpdateAsync (pkexec-Aufruf)"
```

---

### Task 7: Ventoy-Install/Update-Commands + Inline-Bestätigung in `LinuxMainViewModel`

**Files:**
- Modify: `Linux/ViewModels/LinuxMainViewModel.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Interfaces:**
- Consumes: `VentoyInstallService.InstallOrUpdateAsync(...)` (Task 6).
- Produces: `LinuxMainViewModel(..., VentoyInstallService? ventoyInstallService = null)` (weiterer optionaler Konstruktor-Parameter), `SecureBootEnabled` (`bool`), `PendingConfirmationMessage` (`string?` — nicht-null schaltet die Inline-Bestätigungsleiste sichtbar), `RequestVentoyInstallCommand`/`RequestVentoyUpdateCommand` (`RelayCommand`, setzen `PendingConfirmationMessage`), `ConfirmVentoyCommand`/`CancelVentoyCommand` (`RelayCommand`), `VentoyStatus` (`string`).

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs` neue Testklasse:

```csharp
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
```

Hinweis zum dritten Test: `ConfirmVentoyCommand_Success_...` prüft, dass `PendingConfirmationMessage` nach Abschluss zurückgesetzt wird — durch das vorab abgelegte Fake-Skript läuft der komplette Ablauf (Skript gefunden → `pkexec`-Fake → Exitcode 0) ohne echtes Netzwerk oder echte Hardware durch.

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelVentoyTests`
Expected: FAIL — `CS1739`/`CS1061` (neue Konstruktor-Parameter/Properties/Commands existieren noch nicht)

- [ ] **Step 3: Implementierung**

Konstruktor um `ventoyInstallService`-Parameter erweitern:

```csharp
        private readonly VentoyInstallService _ventoyInstallService;

        public LinuxMainViewModel(
            IIsoDatabaseService db, string downloadDirectory,
            string? settingsIniPath = null, DownloadFunc? download = null, ResolveFunc? resolve = null,
            LinuxUsbService? usbService = null, VentoyInstallService? ventoyInstallService = null)
        {
            _db = db;
            _downloadDirectory = downloadDirectory;
            _settingsIniPath = settingsIniPath ?? LinuxPaths.SettingsIni;
            _download = download ?? ((url, dest, progress, token) => HttpService.Instance.DownloadAsync(url, dest, progress, token));
            _resolve = resolve ?? (entry => HttpService.Instance.ResolveLatestAsync(entry));
            _usbService = usbService ?? new LinuxUsbService();
            _ventoyInstallService = ventoyInstallService ?? new VentoyInstallService();

            Categories = new ObservableCollection<CategoryOption>();
            Rows = new ObservableCollection<LinuxIsoRow>();
            Drives = new ObservableCollection<LinuxBlockDevice>();
            RebuildCategories();

            RefreshCommand        = new RelayCommand(Refresh);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            DownloadCommand        = new RelayCommand(() => _ = DownloadSelectedAsync(), () => SelectedRow is not null && !IsBusy);
            CopyToStickCommand     = new RelayCommand(() => _ = CopySelectedToStickAsync(), () => SelectedRow is not null && SelectedDrive is not null && !IsBusy);
            RequestVentoyInstallCommand = new RelayCommand(() => RequestVentoyConfirmation(updateMode: false), () => SelectedDrive is not null && !IsBusy);
            RequestVentoyUpdateCommand  = new RelayCommand(() => RequestVentoyConfirmation(updateMode: true),  () => SelectedDrive is not null && !IsBusy);
            ConfirmVentoyCommand   = new RelayCommand(() => _ = ConfirmVentoyAsync(), () => PendingConfirmationMessage is not null && !IsBusy);
            CancelVentoyCommand    = new RelayCommand(() => PendingConfirmationMessage = null, () => PendingConfirmationMessage is not null);

            ApplyFilter();
        }
```

Neue Properties nach `CopyStatus` einfügen:

```csharp
        private bool _secureBootEnabled;
        public bool SecureBootEnabled
        {
            get => _secureBootEnabled;
            set => SetField(ref _secureBootEnabled, value);
        }

        private string? _pendingConfirmationMessage;
        public string? PendingConfirmationMessage
        {
            get => _pendingConfirmationMessage;
            private set
            {
                if (SetField(ref _pendingConfirmationMessage, value))
                {
                    ConfirmVentoyCommand.RaiseCanExecuteChanged();
                    CancelVentoyCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _pendingUpdateMode;

        private string _ventoyStatus = string.Empty;
        public string VentoyStatus
        {
            get => _ventoyStatus;
            private set => SetField(ref _ventoyStatus, value);
        }
```

Neue Command-Properties nach `CopyToStickCommand`:

```csharp
        public RelayCommand RequestVentoyInstallCommand { get; }
        public RelayCommand RequestVentoyUpdateCommand { get; }
        public RelayCommand ConfirmVentoyCommand { get; }
        public RelayCommand CancelVentoyCommand { get; }
```

Neue Methoden am Ende der Klasse:

```csharp
        private void RequestVentoyConfirmation(bool updateMode)
        {
            if (SelectedDrive is null) return;
            _pendingUpdateMode = updateMode;
            double sizeGb = SelectedDrive.SizeBytes / 1_000_000_000.0;
            PendingConfirmationMessage = updateMode
                ? $"Ventoy auf {SelectedDrive.DeviceNode} ({sizeGb:F1} GB) aktualisieren?"
                : $"ACHTUNG: Alle Daten auf {SelectedDrive.DeviceNode} ({sizeGb:F1} GB) werden gelöscht. Ventoy jetzt einrichten?";
        }

        public async Task ConfirmVentoyAsync()
        {
            if (SelectedDrive is null || IsBusy) { PendingConfirmationMessage = null; return; }
            string deviceNode = SelectedDrive.DeviceNode;
            bool updateMode = _pendingUpdateMode;
            bool secureBoot = SecureBootEnabled;
            PendingConfirmationMessage = null;

            IsBusy = true;
            try
            {
                bool ok = await _ventoyInstallService.InstallOrUpdateAsync(
                    deviceNode, updateMode, secureBoot,
                    onLog: line => VentoyStatus = line,
                    CancellationToken.None).ConfigureAwait(true);
                VentoyStatus = ok ? "Ventoy-Vorgang abgeschlossen." : "Ventoy-Vorgang fehlgeschlagen.";
            }
            finally
            {
                IsBusy = false;
            }
        }
```

`using System.Threading;` zur `using`-Liste ergänzen (für `CancellationToken`, falls noch nicht vorhanden — aus Task 9 von Phase 1 bereits vorhanden).

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelVentoyTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Volle Linux- und Windows-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet test ULM.Tests && dotnet build Linux/ULM.Linux.csproj`
Expected: alle grün/fehlerfrei

- [ ] **Step 6: Commit**

```bash
git add Linux/ViewModels/LinuxMainViewModel.cs Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs
git commit -m "feat(linux): Ventoy-Install/Update-Commands mit Inline-Bestaetigung"
```

---

### Task 8: UI-Wiring (`MainWindow.axaml`) + Geräte-Polling-Timer (`App.axaml.cs`)

**Files:**
- Modify: `Linux/Views/MainWindow.axaml`
- Modify: `Linux/App.axaml.cs`

**Interfaces:**
- Consumes: alle in Task 3/4/7 ergänzten `LinuxMainViewModel`-Bindings.
- Kein automatisierter UI-Test (Projekt-Konvention, siehe Phase-1-Plan) — Verifikation über Build + manuellen Test (Task 9).

- [ ] **Step 1: `MainWindow.axaml` um USB/Ventoy-Bereich erweitern**

In `Linux/Views/MainWindow.axaml`, den bestehenden `DockPanel.Dock="Bottom"`-Grid-Block (Download-Fortschritt) erweitern — komplette Datei durch folgende Version ersetzen:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:ULM.Linux.ViewModels"
        x:Class="ULM.Linux.Views.MainWindow"
        x:DataType="vm:LinuxMainViewModel"
        Title="Universal Linux Manager"
        Width="900" Height="640">
    <DockPanel Margin="8">
        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto,Auto" Margin="0,0,0,8">
            <TextBox Grid.Column="0" Watermark="{Binding SearchPlaceholder}" Text="{Binding SearchText}" Margin="0,0,8,0" />
            <Button Grid.Column="1" Content="{Binding RefreshLabel}" Command="{Binding RefreshCommand}" Margin="0,0,8,0" />
            <Button Grid.Column="2" Content="{Binding LanguageButtonLabel}" Command="{Binding ToggleLanguageCommand}" />
        </Grid>

        <StackPanel DockPanel.Dock="Bottom" Margin="0,8,0,0" Spacing="6">
            <ProgressBar Minimum="0" Maximum="100" Value="{Binding DownloadPercent}" Height="6" IsVisible="{Binding IsBusy}" />
            <TextBlock Text="{Binding DownloadStatus}" />
            <TextBlock Text="{Binding CopyStatus}" />
            <TextBlock Text="{Binding VentoyStatus}" />

            <Border IsVisible="{Binding PendingConfirmationMessage, Converter={x:Static ObjectConverters.IsNotNull}}"
                    Background="#3A2A00" Padding="8" CornerRadius="4">
                <StackPanel Spacing="6">
                    <TextBlock Text="{Binding PendingConfirmationMessage}" TextWrapping="Wrap" />
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Content="Ja, fortfahren" Command="{Binding ConfirmVentoyCommand}" />
                        <Button Content="Abbrechen" Command="{Binding CancelVentoyCommand}" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto">
                <ComboBox Grid.Column="0" ItemsSource="{Binding Drives}" SelectedItem="{Binding SelectedDrive}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding DeviceNode}" />
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
                <CheckBox Grid.Column="1" Content="Secure Boot" IsChecked="{Binding SecureBootEnabled}" Margin="8,0" />
                <Button Grid.Column="2" Content="Auf Stick kopieren" Command="{Binding CopyToStickCommand}" Margin="0,0,8,0" />
                <Button Grid.Column="3" Content="Ventoy einrichten" Command="{Binding RequestVentoyInstallCommand}" Margin="0,0,8,0" />
                <Button Grid.Column="4" Content="Ventoy aktualisieren" Command="{Binding RequestVentoyUpdateCommand}" />
            </Grid>

            <Button Content="Download" Command="{Binding DownloadCommand}" HorizontalAlignment="Right" />
        </StackPanel>

        <Grid ColumnDefinitions="160,*">
            <ListBox Grid.Column="0" ItemsSource="{Binding Categories}" SelectedItem="{Binding SelectedCategory}" Margin="0,0,8,0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Label}" />
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <ListBox Grid.Column="1" ItemsSource="{Binding Rows}" SelectedItem="{Binding SelectedRow}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="*,Auto,Auto">
                            <TextBlock Grid.Column="0" Text="{Binding Name}" />
                            <TextBlock Grid.Column="1" Text="{Binding SizeLabel}" Margin="8,0" />
                            <TextBlock Grid.Column="2" Text="{Binding StatusLabel}" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Grid>
    </DockPanel>
</Window>
```

Hinweis: `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"` ist bereits vorhanden; `ObjectConverters.IsNotNull` ist Teil von `Avalonia.Data.Converters` (Standard-Namespace, kein zusätzlicher `xmlns`-Import nötig, da `ObjectConverters` global über `x:Static` mit vollem Namespace referenziert werden könnte — falls der Build hier einen fehlenden Namespace meldet, im Implementierungsschritt `xmlns:conv="clr-namespace:Avalonia.Data.Converters;assembly=Avalonia.Base"` ergänzen und `{x:Static conv:ObjectConverters.IsNotNull}` verwenden).

- [ ] **Step 2: `App.axaml.cs` — Polling-Timer**

`Linux/App.axaml.cs` komplett ersetzen:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;
using ULM.Linux.Views;

namespace ULM.Linux
{
    public partial class App : Application
    {
        private DispatcherTimer? _drivePollTimer;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            Directory.CreateDirectory(LinuxPaths.ConfigDir);
            AppPaths.Instance.Apply(LinuxPaths.DataDir);
            LocalizationService.Initialize(LinuxPaths.SettingsIni);
            IsoDatabaseService.Instance.Load();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewModel = new LinuxMainViewModel(IsoDatabaseService.Instance, AppPaths.Instance.DownloadDir);
                desktop.MainWindow = new MainWindow { DataContext = viewModel };

                _drivePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                _drivePollTimer.Tick += async (_, _) => await viewModel.PollDrivesAsync();
                _drivePollTimer.Start();
                _ = viewModel.PollDrivesAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 3: Build verifizieren**

Run: `dotnet build Linux/ULM.Linux.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` — falls `ObjectConverters.IsNotNull` einen Namespace-Fehler wirft, wie im Hinweis zu Step 1 beschrieben korrigieren und erneut bauen.

- [ ] **Step 4: Linux-Publish verifizieren**

Run: `dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`
Expected: `Build succeeded`

- [ ] **Step 5: Volle Test-Suiten ein letztes Mal prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet test ULM.Tests`
Expected: beide `Passed! - Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add Linux/Views/MainWindow.axaml Linux/App.axaml.cs
git commit -m "feat(linux): USB/Ventoy-UI und Geraete-Polling-Timer verdrahtet"
```

---

### Task 9: Manuelle Verifikation auf Linux Mint — Übergabe

**Files:** keine Code-Änderungen.

- [ ] **Step 1: Publizierte Binary bereitstellen**

Run: `dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o Linux/publish-out`
Expected: `Linux/publish-out/ulm-linux` existiert.

- [ ] **Step 2: `pkexec` prüfen (Voraussetzung, keine ULM-Aufgabe)**

Hinweis für den Nutzer: `pkexec` ist auf Linux Mint standardmäßig vorinstalliert (Teil von `policykit-1`, Cinnamon-Desktop-Abhängigkeit) — kein zusätzlicher Installationsschritt zu erwarten, aber falls der Passwort-Dialog beim Klick auf "Ventoy einrichten" gar nicht erscheint, zuerst `which pkexec` im Terminal prüfen.

- [ ] **Step 3: Binary übergeben, Checkliste**

1. `chmod +x ulm-linux`, `./ulm-linux` starten.
2. Bereits eingerichteten Ventoy-Stick einstecken → sollte in der Geräte-Auswahl erscheinen (alle 8s aktualisiert).
3. Eine ISO auswählen, "Auf Stick kopieren" klicken → Datei sollte im passenden Kategorie-Ordner auf dem Stick landen, Ventoy-Bootmenü sollte sie nach einem Reboot/erneuten Boot vom Stick zeigen.
4. Frischen/leeren USB-Stick einstecken, in der Geräte-Auswahl wählen, "Ventoy einrichten" klicken → Bestätigungsleiste sollte Gerätepfad+Größe zeigen, nach Bestätigung sollte ein `pkexec`-Passwort-Dialog erscheinen.
5. Nach Passwort-Eingabe: Ergebnis abwarten, `VentoyStatus`-Zeile beobachten (Rohtext von `Ventoy2Disk.sh`, Fortschritt evtl. nicht in Prozent, sondern als Log-Zeilen — bitte hier besonders genau beschreiben, was tatsächlich angezeigt wird, das ist der Teil mit der größten Unsicherheit).
6. Nach Abschluss: Stick erneut einstecken/prüfen, ob `IsVentoyInstalled` jetzt `true` liefert (Gerät sollte "Ventoy aktualisieren" statt "Ventoy einrichten" anbieten) und ob das ULM-Theme/Bootmenü korrekt erscheint.

- [ ] **Step 4: Ergebnis abwarten**

Kein automatisierter Schritt — Ergebnis der manuellen Prüfung durch den Nutzer abwarten. Bei Abweichungen (insbesondere Schritt 5) wird die betroffene Stelle in `VentoyInstallService`/`LinuxMainViewModel` gezielt nachgebessert, analog zum Vorgehen bei den drei in Phase 1 gefundenen Bugs.

---

## Self-Review (durchgeführt)

- **Spec-Abdeckung:** Alle Punkte aus "Umfang von Phase 2" sind abgedeckt: `lsblk`-Erkennung (Task 1-3), Kopieren auf bestehenden Stick (Task 4), Ventoy-Installation/-Update inkl. Download/Entpacken (Task 5-6), Inline-Bestätigung (Task 7), Secure-Boot-Checkbox (Task 8), UI-Verdrahtung (Task 8), manuelle Verifikation (Task 9).
- **Platzhalter-Scan:** Keine TBD/TODO-Stellen. Die in Task 5 zunächst eingefügte Platzhalter-Methode `DefaultRunElevatedAsync` wird in Task 6 explizit vollständig ersetzt (nicht "später ausfüllen" — der Ersetzungsschritt ist konkret ausformuliert).
- **Typ-Konsistenz geprüft:** `LinuxMainViewModel`-Konstruktor-Parameter werden über Task 3 → 7 konsistent erweitert (jeweils ans Ende angehängt, bestehende Aufrufe bleiben gültig). `VentoyInstallService`-Delegate-Namen (`FetchLatestUrlFunc`, `DownloadFunc`, `RunElevatedFunc`) sind zwischen Task 5 und Task 6 identisch. `LinuxBlockDevice`-Properties (`DeviceNode`, `MountPoint`, `SizeBytes`, `Model`, `IsVentoyInstalled`) werden in Task 1 definiert und in Task 3/4/7 unverändert genutzt.
- **Bekannte Restrisiken** (im Plan an den jeweiligen Stellen markiert, nicht verschwiegen): exakte `lsblk -J`-Feldtypen je util-linux-Version (Task 1, defensiv geparst), exakte Fortschritts-Textform von `Ventoy2Disk.sh` (Task 9, Step 5), Mount-Timing der neuen Ventoy-Partition nach Installation (in der Spec als offene Frage vermerkt, in diesem Plan nicht weiter automatisiert — `EnsureVentoyTheme`-Aufruf nach erfolgreicher Installation ist bewusst nicht Teil dieses Plans, da er ohne echten Testlauf nicht zuverlässig getimt werden kann; wird als Nachtrag ergänzt, sobald Task 9 den tatsächlichen Ablauf bestätigt oder korrigiert).
