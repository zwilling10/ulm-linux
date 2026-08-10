# ULM Linux GUI Phase 1 (Katalog + Download + Zweisprachigkeit) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein neues, eigenständiges Avalonia-Projekt `Linux/ULM.Linux.csproj` liefert eine self-contained `linux-x64`-Binary mit ISO-Katalog, Kategorie-Filter, Suche, Download und DE/EN-Sprachumschalter — ohne die bestehende Windows/WPF-App zu berühren.

**Architecture:** Neues Projekt bindet exakt benannte, bereits plattformneutrale Dateien aus `Core/`, `Infrastructure/` und `ViewModels/ViewModelBase.cs` per `<Compile Include>` (Quelltext-Zweitverwendung, kein `ProjectReference` möglich, da das Hauptprojekt `net8.0-windows`/WPF ist). Ein neues, schlankes `LinuxMainViewModel` (kein Wiederverwenden des bestehenden `MainViewModel`, das an `System.Windows.Threading.Dispatcher` hängt) steuert eine einzige Avalonia-`MainWindow.axaml`. Datenablage folgt XDG (`~/.config/ulm/`, `~/.local/share/ulm/`) über einen neuen `LinuxPaths`-Resolver plus dem bereits vorhandenen `AppPaths.Apply(...)`-Mechanismus.

**Tech Stack:** .NET 8, Avalonia 11.3.18 (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent), xUnit 2.9.2 (Testprojekt, gleiche Version wie `ULM.Tests`).

## Global Constraints

- Keine Änderung an `UniversalLinuxManager.csproj`, an bestehenden `Views/*`/`ViewModels/*` (WPF) oder an bestehendem Verhalten der Windows-App. Einzige Ausnahme: eine additive, rückwärtskompatible Erweiterung von `Infrastructure/LocalizationService.cs` (neuer Overload, bestehende Signaturen bleiben unverändert) — siehe Task 1.
- Bestehende Testsuite `ULM.Tests` (198 Tests) muss nach jedem Task weiterhin vollständig grün sein.
- Keine Windows-spezifischen APIs (`System.Windows.*`, `System.Management`, `Verb="runas"`) in irgendeiner neuen Linux-Datei.
- Kein AppImage, keine USB-/Ventoy-Funktion in Phase 1 (explizit Phase 2, siehe Spec).
- Datenablage: XDG (`~/.config/ulm/`, `~/.local/share/ulm/`), nicht das Windows-Portable-Muster.
- Verteilung: `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true` — eine Datei, kein Installer.
- Spec-Referenz: `docs/superpowers/specs/2026-07-28-linux-gui-design.md`.

---

## Datei-Übersicht

**Neu (Linux-Projekt):**
- `Linux/ULM.Linux.csproj`
- `Linux/Program.cs`
- `Linux/App.axaml`, `Linux/App.axaml.cs`
- `Linux/LinuxPaths.cs`
- `Linux/RelayCommand.cs`
- `Linux/ViewModels/LinuxIsoRow.cs`
- `Linux/ViewModels/LinuxMainViewModel.cs`
- `Linux/Views/MainWindow.axaml`, `Linux/Views/MainWindow.axaml.cs`

**Neu (Testprojekt):**
- `Linux/ULM.Linux.Tests/ULM.Linux.Tests.csproj`
- `Linux/ULM.Linux.Tests/TestDoubles.cs`
- `Linux/ULM.Linux.Tests/LinuxPathsTests.cs`
- `Linux/ULM.Linux.Tests/LinuxIsoRowTests.cs`
- `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Geändert (geteilter Code, minimal):**
- `Infrastructure/LocalizationService.cs` (neuer Overload)
- `Infrastructure/Str.cs` (5 neue Enum-Werte)
- `UniversalLinuxManager.csproj` (Ausführung Task 8 entdeckt: impliziter Compile-Glob des
  Hauptprojekts sammelte `Linux\**` versehentlich mit ein, sobald der Ordner existierte — Build
  brach mit `CS0246`/fehlenden xunit-Typen. Vier `<Compile/None/Content/EmbeddedResource
  Remove="Linux\**">`-Zeilen ergänzt, exakt nach dem bestehenden `ULM.Tests\**`-Muster. Keine
  Verhaltensänderung der Windows-App, reine Build-Abgrenzung.)

**Per `<Compile Include>` unverändert wiederverwendet (keine Änderung):**
- `Core/Models/IsoEntry.cs`, `Core/Models/Constants.cs`
- `Core/Services/HttpService.cs`, `Core/Services/HttpService.DistroResolvers.cs` (Ausführung Task 3:
  `HttpService` ist eine `partial class`, `HttpService.cs` allein löste `CS0103` für alle
  `Resolve*Async`-Aufrufe aus — zweite Datei ist ebenfalls WPF-frei, ergänzt)
- `Core/Services/IsoDatabaseService.cs` (Planungslücke, in Task 4 beim Testprojekt-Build entdeckt
  und nachgetragen — `IIsoDatabaseService`/`IsoDatabaseService` fehlten in der ursprünglichen
  Compile-Liste, obwohl `LinuxIsoRow`/`LinuxMainViewModel`/`FakeIsoDatabaseService` sie zwingend
  brauchen)
- `Infrastructure/AppLanguage.cs`, `Infrastructure/IniService.cs`, `Infrastructure/AppPaths.cs`
- `ViewModels/ViewModelBase.cs`

---

### Task 1: `LocalizationService.Initialize(string)`-Overload für benutzerdefinierte Pfade

**Files:**
- Modify: `Infrastructure/LocalizationService.cs:16` (direkt nach der bestehenden `Initialize()`-Methode)
- Test: `ULM.Tests/LocalizationServiceTests.cs`

**Interfaces:**
- Produces: `internal static void LocalizationService.Initialize(string settingsIniPath)` — setzt `Current` aus einem beliebigen Ini-Pfad statt immer `AppPaths.Instance.SettingsIni` (fest neben der EXE). Wird von Task 9 (Linux-Startup) benutzt, da `AppPaths.SettingsIni` nicht auf XDG-Pfade umleitbar ist.

- [ ] **Step 1: Failing Test schreiben**

In `ULM.Tests/LocalizationServiceTests.cs`, direkt nach der Klasse `LocalizationServiceLoadFromIniTests` (vor `[CollectionDefinition("LocalizationCurrent"...)]`), folgende neue Klasse einfügen:

```csharp
[Collection("LocalizationCurrent")]
public class LocalizationServiceInitializeWithPathTests
{
    [Fact]
    public void Initialize_WithCustomPath_SetsCurrentFromThatFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"ulm-loc-init-{Guid.NewGuid():N}.ini");
        try
        {
            IniService.Write(tempFile, "App", "Language", "en");
            LocalizationService.Initialize(tempFile);
            Assert.Equal(AppLanguage.English, LocalizationService.Current);
        }
        finally { File.Delete(tempFile); }
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test ULM.Tests --filter Initialize_WithCustomPath_SetsCurrentFromThatFile`
Expected: FAIL — `CS1501: No overload for method 'Initialize' takes 1 arguments` (Compile-Fehler, da der Overload noch nicht existiert).

- [ ] **Step 3: Minimale Implementierung**

In `Infrastructure/LocalizationService.cs`, direkt nach Zeile 16 (`public static void Initialize() => Current = LoadFromIni(AppPaths.Instance.SettingsIni);`) einfügen:

```csharp
        // Für Aufrufer außerhalb des Windows-Portable-Pfadmodells (z.B. das Linux-GUI-Projekt,
        // das seine Einstellungsdatei über XDG-Verzeichnisse statt AppPaths.Instance.SettingsIni
        // auflöst). AppPaths.SettingsIni ist bewusst nicht umleitbar (siehe AppPaths.cs) — dieser
        // Overload umgeht das für Aufrufer, die ihren eigenen Pfad kennen.
        internal static void Initialize(string settingsIniPath) => Current = LoadFromIni(settingsIniPath);
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test ULM.Tests --filter Initialize_WithCustomPath_SetsCurrentFromThatFile`
Expected: PASS

- [ ] **Step 5: Volle Testsuite prüfen**

Run: `dotnet test ULM.Tests`
Expected: 199/199 PASS (198 bestehende + 1 neuer Test)

- [ ] **Step 6: Commit**

```bash
git add Infrastructure/LocalizationService.cs ULM.Tests/LocalizationServiceTests.cs
git commit -m "feat(linux): LocalizationService.Initialize(path)-Overload fuer XDG-Pfade"
```

---

### Task 2: Neue Str-Werte für die Linux-GUI

**Files:**
- Modify: `Infrastructure/Str.cs` (5 neue Enum-Werte am Ende der Datei)
- Modify: `Infrastructure/LocalizationService.cs` (je 5 neue Einträge in `De` und `En`)
- Test: `ULM.Tests/LocalizationServiceTests.cs`

**Interfaces:**
- Produces: `Str.Linux_Category_All`, `Str.Linux_Toolbar_SearchPlaceholder`, `Str.Linux_Toolbar_Refresh`, `Str.Linux_Download_NoUrl`, `Str.Linux_Download_Failed` — genutzt von `LinuxIsoRow`/`LinuxMainViewModel` (Task 6/7) und `MainWindow.axaml` (Task 8).

- [ ] **Step 1: Failing Test schreiben**

In `ULM.Tests/LocalizationServiceTests.cs`, neue Testklasse am Ende der Datei anfügen:

```csharp
public class LocalizationServiceLinuxStringsTests
{
    [Theory]
    [InlineData(AppLanguage.German, "Alle")]
    [InlineData(AppLanguage.English, "All")]
    public void T_Linux_Category_All_ReturnsCorrectTextForLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, LocalizationService.T(Str.Linux_Category_All, language));
    }

    [Theory]
    [InlineData(AppLanguage.German, "Distro suchen…")]
    [InlineData(AppLanguage.English, "Search distro…")]
    public void T_Linux_Toolbar_SearchPlaceholder_ReturnsCorrectTextForLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, LocalizationService.T(Str.Linux_Toolbar_SearchPlaceholder, language));
    }

    [Theory]
    [InlineData(AppLanguage.German, "Aktualisieren")]
    [InlineData(AppLanguage.English, "Refresh")]
    public void T_Linux_Toolbar_Refresh_ReturnsCorrectTextForLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, LocalizationService.T(Str.Linux_Toolbar_Refresh, language));
    }

    [Theory]
    [InlineData(AppLanguage.German, "Keine Download-URL hinterlegt.")]
    [InlineData(AppLanguage.English, "No download URL configured.")]
    public void T_Linux_Download_NoUrl_ReturnsCorrectTextForLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, LocalizationService.T(Str.Linux_Download_NoUrl, language));
    }

    [Theory]
    [InlineData(AppLanguage.German, "Download fehlgeschlagen.")]
    [InlineData(AppLanguage.English, "Download failed.")]
    public void T_Linux_Download_Failed_ReturnsCorrectTextForLanguage(AppLanguage language, string expected)
    {
        Assert.Equal(expected, LocalizationService.T(Str.Linux_Download_Failed, language));
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test ULM.Tests --filter LocalizationServiceLinuxStringsTests`
Expected: FAIL — `CS0117: 'Str' does not contain a definition for 'Linux_Category_All'` (Compile-Fehler).

- [ ] **Step 3: Minimale Implementierung**

In `Infrastructure/Str.cs`, letzten Enum-Wert vor der schließenden `}` der `enum Str`-Deklaration suchen (Datei endet mit dem letzten Eintrag gefolgt von `}`) und davor einfügen:

```csharp
        // ── Linux-GUI (Phase 1) ─────────────────────────────────────────
        Linux_Category_All,
        Linux_Toolbar_SearchPlaceholder,
        Linux_Toolbar_Refresh,
        Linux_Download_NoUrl,
        Linux_Download_Failed,
```

In `Infrastructure/LocalizationService.cs`, im `De`-Dictionary (endet kurz vor der schließenden `};` des `private static readonly Dictionary<Str, string> De = new() { ... };`-Blocks) vor der letzten schließenden Klammer einfügen:

```csharp
            [Str.Linux_Category_All]                = "Alle",
            [Str.Linux_Toolbar_SearchPlaceholder]    = "Distro suchen…",
            [Str.Linux_Toolbar_Refresh]              = "Aktualisieren",
            [Str.Linux_Download_NoUrl]                = "Keine Download-URL hinterlegt.",
            [Str.Linux_Download_Failed]               = "Download fehlgeschlagen.",
```

Im `En`-Dictionary (analoger Aufbau, direkt nach `De` in derselben Datei) die englischen Gegenstücke an der entsprechenden Stelle einfügen:

```csharp
            [Str.Linux_Category_All]                = "All",
            [Str.Linux_Toolbar_SearchPlaceholder]    = "Search distro…",
            [Str.Linux_Toolbar_Refresh]              = "Refresh",
            [Str.Linux_Download_NoUrl]                = "No download URL configured.",
            [Str.Linux_Download_Failed]               = "Download failed.",
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test ULM.Tests --filter LocalizationServiceLinuxStringsTests`
Expected: PASS (10 Tests: 5 Werte × 2 Sprachen)

- [ ] **Step 5: Volle Testsuite prüfen**

Run: `dotnet test ULM.Tests`
Expected: 209/209 PASS (inkl. `AllStrValues_HaveGermanAndEnglishTranslation`, das jetzt auch die 5 neuen Werte automatisch mitprüft)

- [ ] **Step 6: Commit**

```bash
git add Infrastructure/Str.cs Infrastructure/LocalizationService.cs ULM.Tests/LocalizationServiceTests.cs
git commit -m "feat(linux): neue Str-Werte fuer die Linux-GUI (Kategorie-Filter, Toolbar, Download-Fehler)"
```

---

### Task 3: `Linux/ULM.Linux.csproj` — leeres, publizierbares Avalonia-Grundgerüst

**Files:**
- Create: `Linux/ULM.Linux.csproj`
- Create: `Linux/Program.cs`
- Create: `Linux/App.axaml`
- Create: `Linux/App.axaml.cs`
- Create: `Linux/Views/MainWindow.axaml`
- Create: `Linux/Views/MainWindow.axaml.cs`

**Interfaces:**
- Produces: buildbares/publizierbares Avalonia-Projekt mit leerem Hauptfenster. Spätere Tasks fügen `DataContext`, ViewModel-Bindings und die eigentliche UI hinzu (Task 8/9).
- Consumes: nichts (erster Schritt).

- [ ] **Step 1: Projektordner und `.csproj` anlegen**

`Linux/ULM.Linux.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ULM.Linux</RootNamespace>
    <AssemblyName>ulm-linux</AssemblyName>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>

    <!-- Self-contained Single-File fuer linux-x64, analog zum bestehenden win-x64-Publish in
         UniversalLinuxManager.csproj. Siehe docs/superpowers/specs/2026-07-28-linux-gui-design.md. -->
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.18" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.18" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.18" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="**\*.axaml" />
  </ItemGroup>

  <!-- Erlaubt ULM.Linux.Tests Zugriff auf 'internal' Klassen/Methoden — gleiches Muster wie
       UniversalLinuxManager.csproj es fuer ULM.Tests bereits nutzt. -->
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>ULM.Linux.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <!-- Core/Infrastructure-Dateien als Quelltext eingebunden (gleiche Datei, zweites Projekt) statt
       per ProjectReference — UniversalLinuxManager.csproj ist net8.0-windows/WPF und kann nicht
       plattformneutral referenziert werden. Nur explizit geprüfte, WPF-freie Dateien (siehe
       docs/superpowers/specs/2026-07-28-linux-gui-design.md). -->
  <ItemGroup>
    <Compile Include="..\Core\Models\IsoEntry.cs" Link="Core\Models\IsoEntry.cs" />
    <Compile Include="..\Core\Models\Constants.cs" Link="Core\Models\Constants.cs" />
    <Compile Include="..\Core\Services\HttpService.cs" Link="Core\Services\HttpService.cs" />
    <Compile Include="..\Infrastructure\AppLanguage.cs" Link="Infrastructure\AppLanguage.cs" />
    <Compile Include="..\Infrastructure\Str.cs" Link="Infrastructure\Str.cs" />
    <Compile Include="..\Infrastructure\LocalizationService.cs" Link="Infrastructure\LocalizationService.cs" />
    <Compile Include="..\Infrastructure\IniService.cs" Link="Infrastructure\IniService.cs" />
    <Compile Include="..\Infrastructure\AppPaths.cs" Link="Infrastructure\AppPaths.cs" />
    <Compile Include="..\ViewModels\ViewModelBase.cs" Link="ViewModels\ViewModelBase.cs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Avalonia-Einstiegspunkt**

`Linux/Program.cs`:

```csharp
using Avalonia;

namespace ULM.Linux
{
    internal sealed class Program
    {
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
```

- [ ] **Step 3: `App`-Klasse (noch ohne ViewModel-Wiring, folgt in Task 9)**

`Linux/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ULM.Linux.App">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

`Linux/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ULM.Linux.Views;

namespace ULM.Linux
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 4: Leeres Hauptfenster**

`Linux/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ULM.Linux.Views.MainWindow"
        Title="Universal Linux Manager"
        Width="800" Height="560">
    <TextBlock Text="ULM Linux — Grundgerüst" Margin="16" />
</Window>
```

`Linux/Views/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ULM.Linux.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
```

- [ ] **Step 5: Build verifizieren**

Run: `dotnet build Linux/ULM.Linux.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (Avalonia-Pakete werden beim ersten Build von nuget.org restauriert)

- [ ] **Step 6: Linux-Publish verifizieren (läuft auf dem Windows-Host, cross-kompiliert für linux-x64)**

Run: `dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`
Expected: `Build succeeded`, Ausgabe u.a. `Linux/bin/Release/net8.0/linux-x64/publish/ulm-linux` (eine Datei, keine `.dll`-Begleitdateien außer evtl. `.pdb`)

- [ ] **Step 7: Commit**

```bash
git add Linux/ULM.Linux.csproj Linux/Program.cs Linux/App.axaml Linux/App.axaml.cs Linux/Views/MainWindow.axaml Linux/Views/MainWindow.axaml.cs
git commit -m "feat(linux): Avalonia-Grundgerüst (leeres Fenster, self-contained linux-x64 publizierbar)"
```

---

### Task 4: `Linux/ULM.Linux.Tests` — Testprojekt-Grundgerüst

**Files:**
- Create: `Linux/ULM.Linux.Tests/ULM.Linux.Tests.csproj`
- Create: `Linux/ULM.Linux.Tests/TestDoubles.cs`

**Interfaces:**
- Produces: `ULM.Linux.Tests.FakeIsoDatabaseService : IIsoDatabaseService` — In-Memory-Test-Double, genutzt von Task 6/7-Tests.
- Consumes: `ULM.Core.Services.IIsoDatabaseService`, `ULM.Core.Models.IsoEntry` (aus `ULM.Linux.csproj` kompiliert, per `ProjectReference`).

- [ ] **Step 1: Testprojekt anlegen**

`Linux/ULM.Linux.Tests/ULM.Linux.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ULM.Linux.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Fake-Datenbank-Double**

`Linux/ULM.Linux.Tests/TestDoubles.cs`:

```csharp
using System.Collections.Generic;
using ULM.Core.Models;
using ULM.Core.Services;

namespace ULM.Linux.Tests
{
    internal sealed class FakeIsoDatabaseService : IIsoDatabaseService
    {
        private readonly List<IsoEntry> _entries = new();

        public IReadOnlyList<IsoEntry> Entries => _entries;
        public int Count => _entries.Count;

        public void Load() { }
        public void Save() { }
        public void SaveFilenames() { }
        public void Add(IsoEntry entry) => _entries.Add(entry);
        public void Remove(int index) => _entries.RemoveAt(index);
        public void SaveExpectedSize(IsoEntry entry, long bytes) => entry.ExpectedSizeBytes = bytes;
    }
}
```

- [ ] **Step 3: Platzhalter-Test, um das Projekt lauffähig zu bestätigen**

`Linux/ULM.Linux.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace ULM.Linux.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void FakeIsoDatabaseService_StartsEmpty()
        {
            var db = new FakeIsoDatabaseService();
            Assert.Equal(0, db.Count);
        }
    }
}
```

- [ ] **Step 4: Test ausführen**

Run: `dotnet test Linux/ULM.Linux.Tests`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add Linux/ULM.Linux.Tests/ULM.Linux.Tests.csproj Linux/ULM.Linux.Tests/TestDoubles.cs Linux/ULM.Linux.Tests/SmokeTests.cs
git commit -m "test(linux): Testprojekt-Grundgerüst mit FakeIsoDatabaseService"
```

---

### Task 5: `LinuxPaths` — XDG-Pfad-Resolver

**Files:**
- Create: `Linux/LinuxPaths.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxPathsTests.cs`

**Interfaces:**
- Produces: `ULM.Linux.LinuxPaths.ConfigDir` (`string`), `.DataDir` (`string`), `.SettingsIni` (`string`) — statische Properties, lesen `XDG_CONFIG_HOME`/`XDG_DATA_HOME` mit Fallback auf `~/.config`/`~/.local/share`. Genutzt von Task 9 (App-Startup).

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/LinuxPathsTests.cs`:

```csharp
using System;
using System.IO;
using Xunit;

namespace ULM.Linux.Tests
{
    public class LinuxPathsTests
    {
        [Fact]
        public void ConfigDir_UsesXdgConfigHomeWhenSet()
        {
            string original = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/tmp/xdg-config-test");
                Assert.Equal(Path.Combine("/tmp/xdg-config-test", "ulm"), LinuxPaths.ConfigDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", original); }
        }

        [Fact]
        public void DataDir_UsesXdgDataHomeWhenSet()
        {
            string original = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/xdg-data-test");
                Assert.Equal(Path.Combine("/tmp/xdg-data-test", "ulm"), LinuxPaths.DataDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", original); }
        }

        [Fact]
        public void ConfigDir_FallsBackToHomeConfigWhenUnset()
        {
            string original = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "");
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Assert.Equal(Path.Combine(home, ".config", "ulm"), LinuxPaths.ConfigDir);
            }
            finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", original); }
        }

        [Fact]
        public void SettingsIni_IsUlmSettingsIniInsideConfigDir()
        {
            Assert.Equal(Path.Combine(LinuxPaths.ConfigDir, "ulm_settings.ini"), LinuxPaths.SettingsIni);
        }
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxPathsTests`
Expected: FAIL — `CS0246: The type or namespace name 'LinuxPaths' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/LinuxPaths.cs`:

```csharp
using System;
using System.IO;

namespace ULM.Linux
{
    /// <summary>
    /// XDG-Basisverzeichnisse für die Linux-GUI (siehe
    /// docs/superpowers/specs/2026-07-28-linux-gui-design.md — bewusster Bruch mit dem
    /// Windows-Portable-Muster aus Infrastructure/AppPaths.cs).
    /// </summary>
    public static class LinuxPaths
    {
        public static string ConfigDir => Path.Combine(ResolveXdg("XDG_CONFIG_HOME", ".config"), "ulm");
        public static string DataDir   => Path.Combine(ResolveXdg("XDG_DATA_HOME", ".local/share"), "ulm");

        public static string SettingsIni => Path.Combine(ConfigDir, "ulm_settings.ini");

        private static string ResolveXdg(string envVar, string relativeToHome)
        {
            string? fromEnv = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, relativeToHome);
        }
    }
}
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxPathsTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add Linux/LinuxPaths.cs Linux/ULM.Linux.Tests/LinuxPathsTests.cs
git commit -m "feat(linux): XDG-Pfad-Resolver (LinuxPaths)"
```

---

### Task 6: `RelayCommand` (Linux-eigen, ohne WPF-`CommandManager`)

**Files:**
- Create: `Linux/RelayCommand.cs`
- Test: `Linux/ULM.Linux.Tests/RelayCommandTests.cs`

**Interfaces:**
- Produces: `ULM.Linux.RelayCommand : ICommand` mit `RaiseCanExecuteChanged()`. Genutzt von `LinuxMainViewModel` (Task 7).
- Begründung: `Infrastructure/RelayCommand.cs` nutzt `System.Windows.Input.CommandManager` (WPF-spezifisch, in Avalonia/plattformneutralem `net8.0` nicht verfügbar) — kann nicht per `<Compile Include>` wiederverwendet werden.

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/RelayCommandTests.cs`:

```csharp
using Xunit;

namespace ULM.Linux.Tests
{
    public class RelayCommandTests
    {
        [Fact]
        public void Execute_InvokesAction()
        {
            int calls = 0;
            var cmd = new RelayCommand(() => calls++);
            cmd.Execute(null);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void CanExecute_DefaultsToTrue()
        {
            var cmd = new RelayCommand(() => { });
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_UsesProvidedPredicate()
        {
            bool allowed = false;
            var cmd = new RelayCommand(() => { }, () => allowed);
            Assert.False(cmd.CanExecute(null));
            allowed = true;
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void RaiseCanExecuteChanged_FiresEvent()
        {
            var cmd = new RelayCommand(() => { });
            bool fired = false;
            cmd.CanExecuteChanged += (_, _) => fired = true;
            cmd.RaiseCanExecuteChanged();
            Assert.True(fired);
        }
    }
}
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter RelayCommandTests`
Expected: FAIL — `CS0246: The type or namespace name 'RelayCommand' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/RelayCommand.cs`:

```csharp
using System;
using System.Windows.Input;

namespace ULM.Linux
{
    /// <summary>
    /// Parameterloser ICommand für die Linux-GUI. Bewusst kein Wiederverwenden von
    /// Infrastructure/RelayCommand.cs — dieses nutzt System.Windows.Input.CommandManager
    /// (WPF-spezifisch, hier nicht verfügbar). CanExecuteChanged wird deshalb als reines
    /// C#-Event geführt statt an CommandManager.RequerySuggested zu hängen.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute is null || _canExecute();

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter RelayCommandTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add Linux/RelayCommand.cs Linux/ULM.Linux.Tests/RelayCommandTests.cs
git commit -m "feat(linux): eigenes RelayCommand ohne WPF-CommandManager-Abhaengigkeit"
```

---

### Task 7: `LinuxIsoRow` — Anzeige-Wrapper um `IsoEntry`

**Files:**
- Create: `Linux/ViewModels/LinuxIsoRow.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxIsoRowTests.cs`

**Interfaces:**
- Consumes: `ULM.Core.Models.IsoEntry` (Properties `Name`, `Category`, Methoden `IsLocallyAvailable(string)`, `LocalFileSize(string)`), `ULM.Core.Models.Constants.CategoryLabel(string)`, `LocalizationService.T(Str.Row_Local | Str.Row_NotLocal)`.
- Produces: `ULM.Linux.ViewModels.LinuxIsoRow` mit `Entry` (`IsoEntry`), `Name`, `CategoryKey`, `CategoryLabel`, `SizeLabel`, `StatusLabel` (alle `string`). Genutzt von `LinuxMainViewModel` (Task 8) und `MainWindow.axaml` (Task 9).

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/LinuxIsoRowTests.cs`:

```csharp
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
```

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxIsoRowTests`
Expected: FAIL — `CS0246: The type or namespace name 'LinuxIsoRow' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/ViewModels/LinuxIsoRow.cs`:

```csharp
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.ViewModels
{
    /// <summary>Anzeige-Wrapper: übersetzt IsoEntry-Rohdaten in fertig formatierte UI-Strings.</summary>
    public sealed class LinuxIsoRow
    {
        private readonly string _downloadDirectory;

        public LinuxIsoRow(IsoEntry entry, string downloadDirectory)
        {
            Entry = entry;
            _downloadDirectory = downloadDirectory;
        }

        public IsoEntry Entry { get; }

        public string Name => Entry.Name;
        public string CategoryKey => Entry.Category;
        public string CategoryLabel => Constants.CategoryLabel(Entry.Category);

        public string SizeLabel
        {
            get
            {
                long bytes = Entry.LocalFileSize(_downloadDirectory);
                if (bytes <= 0) return "-";
                double gb = bytes / 1024.0 / 1024.0 / 1024.0;
                return gb >= 0.1 ? $"{gb:F1} GB" : $"{bytes / 1024.0 / 1024.0:F0} MB";
            }
        }

        public string StatusLabel => Entry.IsLocallyAvailable(_downloadDirectory)
            ? LocalizationService.T(Str.Row_Local)
            : LocalizationService.T(Str.Row_NotLocal);
    }
}
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxIsoRowTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Linux/ViewModels/LinuxIsoRow.cs Linux/ULM.Linux.Tests/LinuxIsoRowTests.cs
git commit -m "feat(linux): LinuxIsoRow Anzeige-Wrapper (Kategorie-Label, Groesse, Status)"
```

---

### Task 8: `LinuxMainViewModel` — Katalog, Filter, Suche, Sprachumschalter

**Files:**
- Create: `Linux/ViewModels/LinuxMainViewModel.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Interfaces:**
- Consumes: `IIsoDatabaseService` (`Entries`, `Load()`), `LinuxIsoRow` (Task 7), `ULM.Linux.RelayCommand` (Task 6), `ULM.ViewModels.ViewModelBase` (`SetField`, `OnPropertyChanged`), `Constants.Categories`/`CategoryLabel`, `LocalizationService.T`/`Current`/`SetLanguage`, `LinuxPaths.SettingsIni` (Task 5).
- Produces: `ULM.Linux.ViewModels.LinuxMainViewModel` mit `Categories` (`ObservableCollection<CategoryOption>`), `SelectedCategory`, `SearchText`, `Rows` (`ObservableCollection<LinuxIsoRow>`), `SelectedRow`, `RefreshCommand`, `ToggleLanguageCommand`, `LanguageButtonLabel`, `SearchPlaceholder`, `RefreshLabel`. Genutzt von `MainWindow.axaml` (Task 9) und `App.axaml.cs` (Task 10). Download-Funktionalität folgt in Task 9 als eigener, unabhängig testbarer Teil (`DownloadCommand`).

- [ ] **Step 1: Failing Test schreiben**

`Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`:

```csharp
using System.Linq;
using ULM.Core.Models;
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
    }

    [Xunit.CollectionDefinition("LinuxLocalizationCurrent", DisableParallelization = true)]
    public class LinuxLocalizationCurrentCollection { }
}
```

Hinweis: die `DisableParallelization`-Collection ist nötig, weil `LocalizationService.Current` (aus `Infrastructure/LocalizationService.cs`, per `<Compile Include>` in `ULM.Linux` mitkompiliert) globaler statischer Zustand ist — exakt dasselbe Muster wie `LocalizationCurrentCollection` in `ULM.Tests/LocalizationServiceTests.cs`.

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelTests`
Expected: FAIL — `CS0246: The type or namespace name 'LinuxMainViewModel' could not be found`

- [ ] **Step 3: Implementierung**

`Linux/ViewModels/LinuxMainViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.ViewModels;

namespace ULM.Linux.ViewModels
{
    public sealed record CategoryOption(string? Key, string Label);

    public sealed class LinuxMainViewModel : ViewModelBase
    {
        private readonly IIsoDatabaseService _db;
        private readonly string _downloadDirectory;
        private readonly string _settingsIniPath;

        public LinuxMainViewModel(IIsoDatabaseService db, string downloadDirectory, string? settingsIniPath = null)
        {
            _db = db;
            _downloadDirectory = downloadDirectory;
            _settingsIniPath = settingsIniPath ?? LinuxPaths.SettingsIni;

            Categories = new ObservableCollection<CategoryOption>();
            Rows = new ObservableCollection<LinuxIsoRow>();
            RebuildCategories();
            _selectedCategory = Categories[0];

            RefreshCommand        = new RelayCommand(Refresh);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);

            ApplyFilter();
        }

        public ObservableCollection<CategoryOption> Categories { get; }
        public ObservableCollection<LinuxIsoRow> Rows { get; }

        private CategoryOption _selectedCategory;
        public CategoryOption SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetField(ref _selectedCategory, value)) ApplyFilter(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetField(ref _searchText, value)) ApplyFilter(); }
        }

        private LinuxIsoRow? _selectedRow;
        public LinuxIsoRow? SelectedRow
        {
            get => _selectedRow;
            set => SetField(ref _selectedRow, value);
        }

        public string LanguageButtonLabel => LocalizationService.Current == AppLanguage.German ? "EN" : "DE";
        public string SearchPlaceholder   => LocalizationService.T(Str.Linux_Toolbar_SearchPlaceholder);
        public string RefreshLabel        => LocalizationService.T(Str.Linux_Toolbar_Refresh);

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ToggleLanguageCommand { get; }

        public void Refresh()
        {
            _db.Load();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Rows.Clear();
            IEnumerable<IsoEntry> entries = _db.Entries;
            if (SelectedCategory.Key is not null)
                entries = entries.Where(e => e.Category == SelectedCategory.Key);
            if (!string.IsNullOrWhiteSpace(SearchText))
                entries = entries.Where(e => e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            foreach (IsoEntry entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new LinuxIsoRow(entry, _downloadDirectory));
        }

        private void RebuildCategories()
        {
            string? previousKey = Categories.Count > 0 ? _selectedCategory?.Key : null;
            Categories.Clear();
            Categories.Add(new CategoryOption(null, LocalizationService.T(Str.Linux_Category_All)));
            foreach (string key in Constants.Categories)
                Categories.Add(new CategoryOption(key, Constants.CategoryLabel(key)));
            if (previousKey is not null)
                _selectedCategory = Categories.First(c => c.Key == previousKey);
        }

        public void ToggleLanguage()
        {
            AppLanguage next = LocalizationService.Current == AppLanguage.German ? AppLanguage.English : AppLanguage.German;
            LocalizationService.SetLanguage(next, _settingsIniPath);

            RebuildCategories();
            OnPropertyChanged(nameof(Categories));
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(LanguageButtonLabel));
            OnPropertyChanged(nameof(SearchPlaceholder));
            OnPropertyChanged(nameof(RefreshLabel));
            ApplyFilter();
        }
    }
}
```

Hinweis zu `LocalizationService.SetLanguage(AppLanguage, string)`: das ist der bestehende `internal`-Overload aus `Infrastructure/LocalizationService.cs:38` (unverändert, kein neuer Code nötig) — durch `<Compile Include>` in `ULM.Linux` verfügbar.

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelTests`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Volle Linux-Testsuite + Windows-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet test ULM.Tests`
Expected: beide `Passed! - Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add Linux/ViewModels/LinuxMainViewModel.cs Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs
git commit -m "feat(linux): LinuxMainViewModel (Katalog, Kategorie-/Suchfilter, Sprachumschalter)"
```

---

### Task 9: Download-Funktionalität in `LinuxMainViewModel`

**Files:**
- Modify: `Linux/ViewModels/LinuxMainViewModel.cs`
- Test: `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`

**Interfaces:**
- Consumes: `IsoEntry.AllDownloadUrls()` (bestehend, `Core/Models/IsoEntry.cs`).
- Produces: `LinuxMainViewModel.DownloadFunc` (`delegate System.Threading.Tasks.Task<bool> DownloadFunc(string url, string destPath, IProgress<(int Percent, string Detail)>? progress, CancellationToken token)`), neuer Konstruktor-Parameter `DownloadFunc? download = null` (Default: `HttpService.Instance.DownloadAsync`), `DownloadCommand` (`RelayCommand`), `IsBusy`, `DownloadPercent`, `DownloadStatus`.

- [ ] **Step 1: Failing Test schreiben**

In `Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs`, in der Klasse `LinuxMainViewModelTests` ergänzen:

```csharp
        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_NoUrl_SetsNoUrlStatusAndDoesNotCallDownload()
        {
            var db = BuildDb();
            bool downloadCalled = false;
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                (url, dest, progress, token) => { downloadCalled = true; return System.Threading.Tasks.Task.FromResult(true); });
            vm.SelectedRow = vm.Rows.First();

            await vm.DownloadSelectedAsync();

            Assert.False(downloadCalled);
            Assert.Equal(LocalizationService.T(Str.Linux_Download_NoUrl), vm.DownloadStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_WithUrl_CallsDownloadAndReportsProgress()
        {
            var db = BuildDb();
            db.Entries[0].Url = "https://example.invalid/ubuntu.iso";
            db.Entries[0].Filename = "ubuntu.iso";
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                (url, dest, progress, token) =>
                {
                    progress?.Report((50, "50%"));
                    return System.Threading.Tasks.Task.FromResult(true);
                });
            vm.SelectedRow = vm.Rows.First();

            await vm.DownloadSelectedAsync();

            Assert.Equal(50, vm.DownloadPercent);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadCommand_AllUrlsFail_SetsFailedStatus()
        {
            var db = BuildDb();
            db.Entries[0].Url = "https://example.invalid/ubuntu.iso";
            db.Entries[0].Filename = "ubuntu.iso";
            var vm = new LinuxMainViewModel(db, "/tmp/ulm-linux-vm-test", null,
                (url, dest, progress, token) => System.Threading.Tasks.Task.FromResult(false));
            vm.SelectedRow = vm.Rows.First();

            await vm.DownloadSelectedAsync();

            Assert.Equal(LocalizationService.T(Str.Linux_Download_Failed), vm.DownloadStatus);
        }
```

Konstruktor-Aufrufe in den bereits bestehenden Tests der Klasse (`new LinuxMainViewModel(db, "...")` bzw. mit `settingsIniPath`) bleiben unverändert gültig, da die beiden neuen Parameter (`settingsIniPath`, `download`) optional mit Default-Werten ergänzt werden (Reihenfolge: `db, downloadDirectory, settingsIniPath = null, download = null`).

- [ ] **Step 2: Test ausführen, Fehlschlag bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelTests`
Expected: FAIL — `CS1729`/`CS0117` (Konstruktor-Überladung bzw. `DownloadSelectedAsync`/`DownloadPercent`/`DownloadStatus`/`IsBusy` existieren noch nicht)

- [ ] **Step 3: Implementierung**

In `Linux/ViewModels/LinuxMainViewModel.cs` die `using`-Liste erweitern und die Klasse ergänzen:

```csharp
using System.Threading;
using System.Threading.Tasks;
```

Konstruktor-Signatur ersetzen:

```csharp
        public delegate Task<bool> DownloadFunc(string url, string destPath, IProgress<(int Percent, string Detail)>? progress, CancellationToken token);

        private readonly DownloadFunc _download;

        public LinuxMainViewModel(
            IIsoDatabaseService db, string downloadDirectory,
            string? settingsIniPath = null, DownloadFunc? download = null)
        {
            _db = db;
            _downloadDirectory = downloadDirectory;
            _settingsIniPath = settingsIniPath ?? LinuxPaths.SettingsIni;
            _download = download ?? ((url, dest, progress, token) => HttpService.Instance.DownloadAsync(url, dest, progress, token));

            Categories = new ObservableCollection<CategoryOption>();
            Rows = new ObservableCollection<LinuxIsoRow>();
            RebuildCategories();
            _selectedCategory = Categories[0];

            RefreshCommand        = new RelayCommand(Refresh);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            DownloadCommand        = new RelayCommand(() => _ = DownloadSelectedAsync(), () => SelectedRow is not null && !IsBusy);

            ApplyFilter();
        }
```

Neue Properties und Command direkt nach `SelectedRow` einfügen:

```csharp
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetField(ref _isBusy, value)) DownloadCommand.RaiseCanExecuteChanged(); }
        }

        private int _downloadPercent;
        public int DownloadPercent
        {
            get => _downloadPercent;
            private set => SetField(ref _downloadPercent, value);
        }

        private string _downloadStatus = string.Empty;
        public string DownloadStatus
        {
            get => _downloadStatus;
            private set => SetField(ref _downloadStatus, value);
        }

        public RelayCommand DownloadCommand { get; }
```

`SelectedRow`-Setter so anpassen, dass er `DownloadCommand.RaiseCanExecuteChanged()` mit aufruft:

```csharp
        private LinuxIsoRow? _selectedRow;
        public LinuxIsoRow? SelectedRow
        {
            get => _selectedRow;
            set { if (SetField(ref _selectedRow, value)) DownloadCommand.RaiseCanExecuteChanged(); }
        }
```

Neue Methode `DownloadSelectedAsync` am Ende der Klasse (vor der schließenden `}`) einfügen:

```csharp
        public async Task DownloadSelectedAsync()
        {
            if (SelectedRow is null || IsBusy) return;
            IsoEntry entry = SelectedRow.Entry;
            var urls = entry.AllDownloadUrls().ToList();
            if (urls.Count == 0)
            {
                DownloadStatus = LocalizationService.T(Str.Linux_Download_NoUrl);
                return;
            }

            IsBusy = true;
            DownloadPercent = 0;
            try
            {
                string destPath = System.IO.Path.Combine(_downloadDirectory, entry.Filename);
                var progress = new Progress<(int Percent, string Detail)>(p =>
                {
                    DownloadPercent = p.Percent;
                    DownloadStatus  = p.Detail;
                });

                bool ok = false;
                foreach (string candidate in urls)
                {
                    ok = await _download(candidate, destPath, progress, CancellationToken.None).ConfigureAwait(true);
                    if (ok) break;
                }

                DownloadStatus = ok ? LocalizationService.T(Str.Row_Local) : LocalizationService.T(Str.Linux_Download_Failed);
            }
            finally
            {
                IsBusy = false;
                ApplyFilter();
            }
        }
```

- [ ] **Step 4: Test ausführen, Erfolg bestätigen**

Run: `dotnet test Linux/ULM.Linux.Tests --filter LinuxMainViewModelTests`
Expected: `Passed! - Failed: 0, Passed: 10`

- [ ] **Step 5: Volle Linux- und Windows-Testsuite prüfen**

Run: `dotnet test Linux/ULM.Linux.Tests && dotnet test ULM.Tests && dotnet build Linux/ULM.Linux.csproj`
Expected: alle drei grün / fehlerfrei

- [ ] **Step 6: Commit**

```bash
git add Linux/ViewModels/LinuxMainViewModel.cs Linux/ULM.Linux.Tests/LinuxMainViewModelTests.cs
git commit -m "feat(linux): Download-Befehl in LinuxMainViewModel (Mirror-Fallback, Fortschritt, Status)"
```

---

### Task 10: `MainWindow.axaml` — vollständige UI + `App.axaml.cs`-Wiring

**Files:**
- Modify: `Linux/Views/MainWindow.axaml`
- Modify: `Linux/Views/MainWindow.axaml.cs`
- Modify: `Linux/App.axaml.cs`

**Interfaces:**
- Consumes: `LinuxMainViewModel` (Task 8/9), `AppPaths.Instance.Apply(string)` (bestehend, `Infrastructure/AppPaths.cs`), `IsoDatabaseService.Instance` (bestehend, `Core/Services/IsoDatabaseService.cs`), `LinuxPaths` (Task 5).
- Kein automatisierter UI-Test (Projekt-Konvention, siehe Spec „Testing"-Abschnitt) — Verifikation über erfolgreichen Build + manuellen Test auf Linux Mint (Task 11).

- [ ] **Step 1: `MainWindow.axaml` vollständig ausbauen**

`Linux/Views/MainWindow.axaml` komplett ersetzen mit:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:ULM.Linux.ViewModels"
        x:Class="ULM.Linux.Views.MainWindow"
        x:DataType="vm:LinuxMainViewModel"
        Title="Universal Linux Manager"
        Width="800" Height="560">
    <DockPanel Margin="8">
        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto,Auto" Margin="0,0,0,8">
            <TextBox Grid.Column="0" Watermark="{Binding SearchPlaceholder}" Text="{Binding SearchText}" Margin="0,0,8,0" />
            <Button Grid.Column="1" Content="{Binding RefreshLabel}" Command="{Binding RefreshCommand}" Margin="0,0,8,0" />
            <Button Grid.Column="2" Content="{Binding LanguageButtonLabel}" Command="{Binding ToggleLanguageCommand}" />
        </Grid>

        <Grid DockPanel.Dock="Bottom" Margin="0,8,0,0" RowDefinitions="Auto,Auto,Auto">
            <ProgressBar Grid.Row="0" Minimum="0" Maximum="100" Value="{Binding DownloadPercent}" Height="6" IsVisible="{Binding IsBusy}" />
            <TextBlock Grid.Row="1" Text="{Binding DownloadStatus}" Margin="0,4,0,0" />
            <Button Grid.Row="2" Content="Download" Command="{Binding DownloadCommand}" Margin="0,8,0,0" HorizontalAlignment="Right" />
        </Grid>

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

`Linux/Views/MainWindow.axaml.cs` bleibt inhaltlich unverändert (nur `AvaloniaXamlLoader.Load(this)` im Konstruktor, kein Code-Behind-Zugriff nötig — alles läuft über Bindings).

- [ ] **Step 2: `App.axaml.cs` — Startup-Wiring**

`Linux/App.axaml.cs` komplett ersetzen mit:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.IO;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;
using ULM.Linux.Views;

namespace ULM.Linux
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            Directory.CreateDirectory(LinuxPaths.ConfigDir);
            AppPaths.Instance.Apply(LinuxPaths.DataDir);
            LocalizationService.Initialize(LinuxPaths.SettingsIni);
            IsoDatabaseService.Instance.Load();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new LinuxMainViewModel(IsoDatabaseService.Instance, AppPaths.Instance.DownloadDir),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 3: Build verifizieren**

Run: `dotnet build Linux/ULM.Linux.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Linux-Publish erneut verifizieren**

Run: `dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`
Expected: `Build succeeded`

- [ ] **Step 5: Volle Testsuiten (Windows + Linux-Projekt) ein letztes Mal prüfen**

Run: `dotnet test ULM.Tests && dotnet test Linux/ULM.Linux.Tests`
Expected: beide `Passed! - Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add Linux/Views/MainWindow.axaml Linux/App.axaml.cs
git commit -m "feat(linux): vollstaendige MainWindow-UI und App-Startup-Wiring (XDG-Pfade, Katalog laden)"
```

---

### Task 11: Manuelle Verifikation auf Linux Mint — Übergabe

**Files:** keine Code-Änderungen — reine Übergabe-Dokumentation für den Nutzer.

- [ ] **Step 1: Publizierte Binary bereitstellen**

Run: `dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o Linux/publish-out`
Expected: `Linux/publish-out/ulm-linux` existiert (eine Datei, keine weiteren `.dll`s außer optional `.pdb`).

- [ ] **Step 2: Binary an den Nutzer übergeben**

Datei `Linux/publish-out/ulm-linux` dem Nutzer bereitstellen (z.B. per `SendUserFile` oder Hinweis auf den lokalen Pfad), mit folgender Checkliste für den manuellen Test auf Linux Mint:

1. Datei auf Linux Mint kopieren, `chmod +x ulm-linux`, `./ulm-linux` ausführen.
2. Prüfen: Fenster öffnet sich, zeigt Kategorien links und die Standard-Distro-Liste rechts (27 Einträge aus `Constants`/`IsoDatabaseService.DefaultDatabase()`).
3. Kategorie in der linken Liste anklicken → rechte Liste filtert korrekt.
4. Suchfeld befüllen → Liste filtert nach Namen.
5. Sprachumschalter-Button (zeigt „EN"/„DE") klicken → Kategorie-Labels und Toolbar-Texte wechseln sofort die Sprache.
6. Einen Eintrag auswählen, „Download" klicken → Fortschrittsbalken bewegt sich, Datei landet unter `~/.local/share/ulm/ISOs/`.
7. Programm beenden, `~/.config/ulm/ulm_settings.ini` und `~/.local/share/ulm/ulm_isos.ini` prüfen — beide sollten existieren und die zuletzt gewählte Sprache bzw. den Katalog enthalten.

- [ ] **Step 3: Ergebnis abwarten**

Kein automatisierter Schritt — Ergebnis der manuellen Prüfung (Schritte 2–7) durch den Nutzer abwarten, bevor Phase 2 (USB-Erkennung, Ventoy) geplant wird.

---

## Self-Review (durchgeführt)

- **Spec-Abdeckung:** Alle in der Spec unter „Umfang von Phase 1" gelisteten Punkte sind abgedeckt: neues Projekt (Task 3), XDG-Datenablage (Task 5, 10), Katalog/Filter/Suche (Task 8), Download (Task 9), Zweisprachigkeit (Task 1, 2, 8), self-contained-Publish ohne AppImage (Task 3, 11). Die in der Spec skizzierte „AppRes.FillCategoryCombo"-Extraktion erwies sich bei genauerer Prüfung als unnötig — `Constants.Categories`/`Constants.CategoryLabel` sind bereits WPF-frei und werden direkt wiederverwendet (Task 7/8); die tatsächlich nötige kleine Erweiterung war stattdessen `LocalizationService.Initialize(string)` (Task 1), die die Spec auf Prosa-Ebene bereits als Datenfluss beschrieben, aber nicht als konkrete API benannt hatte.
- **Platzhalter-Scan:** Keine TBD/TODO-Stellen; jeder Schritt enthält vollständigen, konkreten Code.
- **Typ-Konsistenz geprüft:** `LinuxMainViewModel`-Konstruktor-Signatur ist über Task 8 und Task 9 hinweg konsistent (Task 9 erweitert sie additiv um zwei optionale Parameter, bestehende Aufrufe aus Task 8 bleiben gültig). `RelayCommand.RaiseCanExecuteChanged()` (Task 6) wird in Task 9 exakt so aufgerufen. `LinuxIsoRow`-Property-Namen (`Name`, `CategoryLabel`, `SizeLabel`, `StatusLabel`) stimmen zwischen Task 7-Implementierung, Task 8-Tests und Task 10-XAML-Bindings überein.
