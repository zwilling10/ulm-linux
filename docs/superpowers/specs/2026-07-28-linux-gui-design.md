# ULM unter Linux — Phase 1: Katalog + Download + Zweisprachigkeit — Design

## Kontext

ULM ist bisher eine reine Windows-Anwendung: `TargetFramework=net8.0-windows`,
`UseWPF=true`. WPF läuft technisch nicht unter Linux — das betrifft nicht nur
die UI, sondern auch mehrere fest mit Windows verdrahtete Stellen:
`System.Management`/WMI für die USB-Stick-Erkennung
(`Core/Services/UsbService.cs`), `Verb="runas"` für die UAC-Elevation beim
Ventoy-Install, sowie ein `System.Windows.Application.Current.Shutdown()`
in `Core/Services/SelfUpdateService.cs`.

**Wunsch:** Der Nutzer möchte ULM auch unter Linux (konkret zum Testen: Linux
Mint) benutzen können, ohne dass zur Laufzeit etwas nachgeladen werden muss —
analog zur bestehenden "kein Installer, einfach starten"-Philosophie unter
Windows (self-contained Single-File-EXE).

## Verworfene Alternativen (im Brainstorming besprochen)

- **Wine/Proton** (bestehende Windows-EXE unverändert unter Wine laufen
  lassen): geringster Aufwand, aber WMI funktioniert unter Wine nicht → USB-
  Erkennung fällt komplett aus, UAC/`runas` ist unzuverlässig. Der
  Kern-Use-Case (Stick erkennen, Ventoy einrichten) wäre damit nicht
  nutzbar. Verworfen.
- **Reine Terminal-UI (TUI, z.B. Spectre.Console)**: kleinster Implementierungs-
  aufwand, volle Core-Wiederverwendung, aber unpassend für die
  Haupt-Zielgruppe (Linux-Desktop-Nutzer erwarten für diese Aufgabe ein
  GUI-Fenster, Vorbilder wie balenaEtcher oder Ventoys eigene Linux-GUI
  setzen diesen Standard). Bleibt als mögliche spätere Ergänzung für
  Headless-/SSH-Szenarien im Hinterkopf, ist aber nicht Ziel dieser Spec.
- **Bestehendes `MainViewModel`/`IsoViewModels` plattformneutral umbauen**
  (WPF-`Dispatcher` durch Interface ersetzen, `System.Windows.Media` raus,
  Datei zwischen Windows- und Linux-Projekt teilen): vermeidet Code-
  Duplizierung, greift aber in bereits fertigen, getesteten, gerade erst
  veröffentlichten Windows-Code ein (198/198 Tests, `v2.40.0` released).
  Widerspricht dem Minimal-Impact-Prinzip des Projekts für ein Linux-
  Experiment, dessen Erfolg noch nicht feststeht. Verworfen zugunsten eines
  neuen, schlanken Linux-eigenen ViewModels (siehe Architektur unten).

**Gewählter Ansatz:** natives Linux-GUI mit Avalonia (spiritueller
WPF-Nachfolger, gleiches XAML-/MVVM-Muster, unterstützt
`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` —
exakt das gleiche Verteilungsmodell wie der bestehende `win-x64`-Build).

## Ziel (Gesamtvision, nicht alles Teil von Phase 1)

1. ULM läuft als eigenständige, self-contained Single-File-Binary unter
   Linux (getestet: Linux Mint), ohne .NET-Installation oder sonstiges
   Nachladen zur Laufzeit.
2. Kernfunktionen: ISO-Katalog durchsuchen/filtern, ISOs herunterladen,
   USB-Stick erkennen, Ventoy einrichten — bedienbar per Maus/GUI, nicht nur
   Terminal.
3. Vollständig zweisprachig (Deutsch/Englisch), wie die Windows-Version.

## Umfang von Phase 1 (diese Spec/dieser Plan)

- Neues Projekt `Linux/ULM.Linux.csproj` (Avalonia, `net8.0`, kein
  `UseWPF`), das `Core/**` und `Infrastructure/**` unverändert per
  Compile-Include referenziert — keine Änderung an
  `UniversalLinuxManager.csproj` oder an bestehendem WPF-Code.
- Neues, schlankes `Linux/ViewModels/LinuxMainViewModel.cs`: Katalog laden,
  nach Kategorie/Suchbegriff filtern, Download starten/verfolgen,
  Sprache umschalten.
- Neues `Linux/Views/MainWindow.axaml`: Werkzeugleiste (Suche, Aktualisieren,
  Sprachumschalter), Kategorie-Seitenleiste, ISO-Liste/-Tabelle,
  Download-Fortschrittsbalken unten. Kein USB-Stick-Bereich in der Fußzeile
  (kommt erst in Phase 2).
- Zweisprachigkeit: Wiederverwendung von `Infrastructure/LocalizationService.cs`
  und `Str.cs` unverändert. Für Phase 1 werden nur bereits vorhandene bzw.
  neue, rein Linux-GUI-spezifische `Str`-Einträge gebraucht (z.B.
  Werkzeugleisten-Texte) — keine Änderung an bestehenden Windows-Einträgen.
- Kleine gezielte Verbesserung: Die Kategorie-Label-Übersetzung
  (`AppRes.FillCategoryCombo`) hängt aktuell an WPF-`ComboBoxItem`. Die
  reine Label-Zuordnung (interner Schlüssel → übersetztes Label) wird in
  eine Toolkit-neutrale Hilfsfunktion (z.B. `Infrastructure`) gezogen, die
  beide Projekte nutzen. Kein Verhaltensunterschied für die Windows-App.
- Distribution: `dotnet publish -c Release -r linux-x64 --self-contained
  -p:PublishSingleFile=true` — eine ausführbare Datei, `chmod +x` + Start,
  kein Installer, kein AppImage (bewusst zurückgestellt, siehe unten).

**Ausdrücklich NICHT Teil von Phase 1** (eigener Plan nach Phase-1-Test):

- USB-Stick-Erkennung unter Linux (`lsblk`/`udev` statt WMI).
- Ventoy-Einrichtung unter Linux (`pkexec`-Elevation statt `runas`, Download
  des Linux-Ventoy-Release-Archivs statt des Windows-Archivs — analog zum
  bestehenden `VentoyInstallWorker`-Muster, das schon heute das
  Ventoy-Archiv zur Laufzeit von GitHub lädt statt es zu bündeln).
- AppImage-Paketierung / Desktop-Icon-Integration.
- Alle übrigen Dialoge (Datenbank-Editor, Health-Check, Changelog, …) —
  diese existieren in Phase 1 schlicht nicht in der Linux-GUI.

## Entscheidungen (im Brainstorming geklärt)

- **Zweistufiger Umfang:** Phase 1 = Katalog + Download + Zweisprachigkeit
  (risikoarm, schnell testbar). Phase 2 = USB-Erkennung + Ventoy (höheres
  Risiko, da für Claude ohne echtes Linux-System blind implementiert — soll
  erst angegangen werden, nachdem Phase 1 auf echtem Linux Mint bestätigt
  wurde).
- **Datenablage:** XDG-Standardverzeichnisse statt des Windows-Portable-
  Musters — Einstellungen (`ulm_settings.ini`) unter `~/.config/ulm/`,
  Katalog + heruntergeladene ISOs (`ulm_isos.ini` + `isos/`-Unterordner)
  unter `~/.local/share/ulm/`. Begründung: entspricht dem, was
  Linux-Desktop-Nutzer von einer "echten" installierten App erwarten,
  bewusster Bruch mit dem Windows-Portable-Prinzip.
- **Verteilungsformat:** einfache self-contained Single-File-Binary statt
  AppImage. Begründung: für den allerersten Testlauf möglichst wenige
  bewegliche Teile (kein `appimagetool`, kein `.desktop`-File, kein
  FUSE-Risiko auf neueren Distros). AppImage bleibt eine mögliche spätere
  Politur-Stufe.
- **ViewModel-Strategie:** neues, eigenständiges `LinuxMainViewModel` statt
  Umbau des bestehenden `MainViewModel` — siehe "Verworfene Alternativen"
  oben. Nutzt Avalonias `Dispatcher.UIThread` statt eines injizierten
  WPF-`Dispatcher`.
- **Testverantwortung:** Claude hat kein Linux-System zur Verfügung und kann
  die laufende GUI nicht selbst verifizieren. Build und automatisierte
  Tests laufen hier (Windows-Host, plattformunabhängiges .NET), die
  self-contained `linux-x64`-Binary wird für den manuellen Test durch den
  Nutzer auf Linux Mint bereitgestellt.

## Architektur

```
Linux/
  ULM.Linux.csproj          (net8.0, Avalonia, RuntimeIdentifier linux-x64,
                              SelfContained, PublishSingleFile)
  App.axaml / App.axaml.cs  (Avalonia-Einstiegspunkt, Init von
                              LocalizationService/IsoDatabaseService mit
                              XDG-Pfaden statt AppPaths-Windows-Logik)
  Views/
    MainWindow.axaml(.cs)   (Werkzeugleiste, Kategorien, ISO-Liste,
                              Download-Fortschritt)
  ViewModels/
    LinuxMainViewModel.cs   (Katalog/Filter/Download/Sprache — schlank,
                              nur Phase-1-Funktionsumfang)
```

`Core/**` und `Infrastructure/**` werden unverändert per Compile-Include
referenziert (gleiches Muster wie `UniversalLinuxManager.csproj` es für
`ULM.Tests` schon vormacht) — keine Kopie, keine Änderung an bestehenden
Dateien außer der in "Umfang von Phase 1" genannten Extraktion der
Kategorie-Label-Hilfsfunktion.

## Datenfluss (Phase 1)

1. App-Start → neuer XDG-Pfad-Resolver ermittelt `~/.config/ulm/` und
   `~/.local/share/ulm/`, legt sie bei Bedarf an.
2. `LocalizationService.Initialize(...)` liest `Language` aus
   `ulm_settings.ini` unter `~/.config/ulm/`; fehlt der Wert, wird ein
   Default aus der `LANG`-Umgebungsvariable geraten (analog zur
   bestehenden Windows-Systemsprache-Erkennung).
3. `IsoDatabaseService.LoadFromIni(...)` lädt `ulm_isos.ini` aus
   `~/.local/share/ulm/`; existiert die Datei nicht, wird sie aus
   `DefaultDatabase()` befüllt (unverändertes Verhalten aus `Core/`).
4. `LinuxMainViewModel` bindet Kategorien/Liste an die Avalonia-Views;
   Auswahl/Suche filtert rein clientseitig (keine Core-Änderung nötig).
5. Download-Klick → `HttpService.DownloadFileAsync(...)` (unverändert aus
   `Core/`) schreibt nach `~/.local/share/ulm/isos/`, Fortschritt wird über
   den bestehenden Callback-Mechanismus an die View gebunden.

## Fehlerbehandlung

Keine neue Fehlerbehandlung nötig — Download-Retry/Mirror-Fallback kommt
unverändert aus `HttpService`. Kein `pkexec`/Elevation in Phase 1, da kein
Ventoy-Schritt enthalten ist. Fehlender Schreibzugriff auf
`~/.local/share/ulm/` (unwahrscheinlich unter Standard-XDG-Rechten) wird
nicht gesondert behandelt — würde als normale IO-Exception sichtbar, wie es
Core/Services heute schon für vergleichbare Fälle tut.

## Testing

- Bestehende Testsuite (`ULM.Tests`, 198 Tests) muss weiterhin 198/198 grün
  bleiben — reiner Nachweis, dass Phase 1 keine bestehende Funktionalität
  berührt.
- Neue, kleine Unit-Tests für `LinuxMainViewModel` (Kategorie-/Suchfilter,
  Sprachumschaltung) — reines .NET, läuft plattformunabhängig auf dem
  Windows-Build-Host.
- Kein automatisierter UI-Test (Projekt-Konvention, siehe bereits
  bestehende Specs) — manuelle Verifikation stattdessen.

## Manuelle Verifikation

Kann ausschließlich auf echtem Linux Mint durch den Nutzer erfolgen:
1. `dotnet publish -c Release -r linux-x64 --self-contained
   -p:PublishSingleFile=true` (von Claude auf dem Windows-Host ausgeführt).
2. Resultierende Binary auf Linux Mint kopieren, `chmod +x`, starten.
3. Prüfen: Fenster öffnet sich, Katalog/Kategorien werden angezeigt,
   Suche/Filter funktioniert, ein Download läuft durch und landet unter
   `~/.local/share/ulm/isos/`, Sprachumschalter DE↔EN funktioniert.

## Offene Fragen für Phase 2 (nicht jetzt entscheiden)

- Genaues Vorgehen für `lsblk`/`udev`-basierte USB-Erkennung (welches Tool/
  welche Library, Rechte-Modell).
- `pkexec`-Integration für die Ventoy-Elevation (Policy-Datei nötig?
  GUI-Passwort-Prompt-Verhalten je nach Desktop-Umgebung).
- Ob/wie ein `.desktop`-Eintrag bzw. AppImage nachgezogen wird, sobald
  Phase 1 sich bewährt hat.
