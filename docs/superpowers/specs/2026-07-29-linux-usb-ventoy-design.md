# ULM unter Linux — Phase 2: USB-Erkennung + Ventoy — Design

## Kontext

Phase 1 (`docs/superpowers/specs/2026-07-28-linux-gui-design.md`,
`docs/superpowers/plans/2026-07-28-linux-gui-phase1.md`) hat Katalog, Download und
Zweisprachigkeit unter Linux gebracht und ist auf echtem Linux Mint verifiziert (inkl. zweier
dabei gefundener und behobener Bugs — fehlende `HttpService.ResolveLatestAsync`-Auflösung vor dem
Download, Crash beim Sprachumschalten durch ungeschützten `SelectedCategory`-Zugriff). USB-Stick-
Erkennung und Ventoy-Einrichtung waren dort bewusst ausgeklammert.

**Wunsch:** komplette Funktionalitätsparität zu Windows — ISOs auf einen Ventoy-Stick kopieren
UND einen frischen Stick als Ventoy-Stick neu einrichten, beides in einem Rutsch (nicht weiter in
Unterphasen aufgeteilt).

## Bestandsaufnahme des Windows-Codes (wichtig für die Architektur)

`Core/Services/UsbService.cs` hat bereits einen Nicht-Windows-Zweig in `ListRemovableDrives()`,
der `/media/$USER/` und `/run/media/$USER/` durchsucht. Dieser Zweig ist für Phase 2 **nicht
ausreichend**: er findet nur bereits von der Desktop-Umgebung eingehängte Sticks. Ein wirklich
frischer, unformatierter Stick hat oft kein erkennbares Dateisystem und wird gar nicht automatisch
eingehängt — genau der Stick, den man für eine Ventoy-Neuinstallation braucht, würde also gar nicht
in der Liste auftauchen.

Alle übrigen `UsbService`-Methoden (`IsVentoyInstalled`, `MoveToCategoryFolder`, `ScanStick`,
`ScanStickVerifiedAsync`, `UpdateVentoyMenu`, `EnsureVentoyTheme`, `DriveTotalMb`/`DriveFreeMb`)
arbeiten bereits rein auf dem eingehängten Pfad-String (Parameter `letter`) und sind ohne
Windows-Abhängigkeit — für den "ISOs auf bestehenden Ventoy-Stick kopieren"-Ablauf praktisch
unverändert wiederverwendbar.

`VentoyInstallWorker` (`Core/Workers/Workers.cs`) lädt das offizielle Windows-Ventoy-Release von
GitHub, entpackt es, ruft `Ventoy2Disk.exe VTOYCLI /I /Drive:X: /NOUSBCheck [/NOSB]` bzw. `/U` auf
und pollt Ventoys eigene `cli_percent.txt`/`cli_done.txt`-Statusdateien. Ventoys Linux-Pendant
(`Ventoy2Disk.sh`, Teil desselben GitHub-Release-Archivs, nur `-linux.tar.gz` statt
`-windows.zip`) arbeitet auf einem **rohen Blockgerät** (`/dev/sdb`), nicht auf einem
Laufwerksbuchstaben/Mount-Pfad, und braucht Root-Rechte.

## Verworfene Alternativen (im Brainstorming besprochen)

- **Nur `/media`-Suche beibehalten, frische Sticks aus dem Umfang nehmen**: geringerer Aufwand,
  aber der Nutzer wollte ausdrücklich vollständige Parität inkl. Ventoy-Neuinstallation. Verworfen.
- **`VentoyWeb.sh`** (Ventoys eigene Web-Oberfläche, startet einen lokalen Webserver mit Browser-UI
  für die Installation): nicht als Ein-Klick-Aktion in ULM automatisierbar, würde die
  bestehende Ein-Klick-UX durchbrechen. Verworfen.
- **Nur Anleitung anzeigen, Nutzer führt `Ventoy2Disk.sh` selbst im Terminal aus**: kein
  Elevation-Code nötig, aber keine Funktionsparität zu Windows. Verworfen.

**Gewählter Ansatz:** `lsblk`-basierte Geräte-Enumeration (ersetzt die bisherige `/media`-Suche)
plus `pkexec bash Ventoy2Disk.sh ...` für die eigentliche Installation — analog zu Windows'
`Verb="runas"` für die Elevation, aber schlanker: `pkexec` hebt nur den einen Ventoy-Befehl an,
ULM selbst muss dafür nicht als kompletter zweiter, erhöhter Prozess neu gestartet werden (anders
als unter Windows, wo `App.xaml.cs` dafür extra `Process.Start(... Verb="runas" ...
--ventoy-install ...)` braucht).

## Umfang von Phase 2 (diese Spec/dieser Plan)

- Neue `lsblk`-basierte Geräte-Erkennung (`Linux/LinuxUsbService.cs`), ersetzt/ergänzt die
  bisherige `/media`-Suche für die Linux-GUI. Polling alle 8 Sekunden, analog zum bestehenden
  Windows-Muster (WMI-Polling im selben Intervall).
- ISOs auf einen bereits eingerichteten Ventoy-Stick kopieren — nutzt `UsbService`-Methoden aus
  `Core/` unverändert.
- Ventoy auf einem frischen/nicht eingerichteten Stick neu installieren ODER auf einem bestehenden
  Ventoy-Stick aktualisieren (`Linux/VentoyInstallService.cs`, `pkexec` + `Ventoy2Disk.sh`).
- Sicherheits-Bestätigung vor jeder destruktiven Aktion, direkt im Hauptfenster (kein separates
  Dialog-Fenster) — zeigt immer Gerätepfad UND Größe, nie automatische Geräteauswahl.
- Secure-Boot-Option (Checkbox, steuert das `-s`-Flag von `Ventoy2Disk.sh`), analog zur
  bestehenden Windows-Checkbox.

**Ausdrücklich NICHT Teil von Phase 2:**

- Echtes udev-Event-Monitoring (Hot-Plug-Benachrichtigung ohne Polling) — Polling reicht, ist
  einfacher und entspricht dem, was Windows heute auch tut.
- AppImage/Desktop-Integration (weiterhin zurückgestellt, siehe Phase-1-Spec).
- Eigenes Formatieren eines Datenträgers außerhalb von `Ventoy2Disk.sh` (Windows' `DoFormat` ist
  auf Linux ohnehin schon ein No-Op-Stub — `Ventoy2Disk.sh` übernimmt Partitionierung/Formatierung
  selbst als Teil der Installation).

## Entscheidungen (im Brainstorming geklärt)

- **Umfang:** beides zusammen (Kopieren + Neuinstallation), nicht weiter in Unterphasen
  aufgeteilt — anders als Phase 1, wo der Nutzer bewusst zwei Schritte wollte.
- **Geräte-Erkennung:** `lsblk`-basiert statt reiner `/media`-Suche, damit auch unformatierte,
  nicht eingehängte Sticks für die Ventoy-Neuinstallation sichtbar sind.
- **Elevation:** `pkexec` direkt auf den Ventoy-Befehl, kein Neustart von ULM selbst als
  erhöhter Prozess.
- **Bestätigungs-UX:** inline im Hauptfenster statt eigenem Dialog-Fenster, um kein neues
  Avalonia-Dialogsystem nur für diesen einen Anwendungsfall aufzubauen.
- **Risiko-Rahmen:** Claude hat weder ein Linux-System noch einen physischen USB-Stick zur
  Verfügung. Die destruktive Ventoy-Installation kann ausschließlich vom Nutzer auf echter
  Hardware verifiziert werden. Insbesondere die genaue Fortschritts-Textform von
  `Ventoy2Disk.sh`s Ausgabe und das Timing des Neu-Einhängens der frisch erstellten
  Ventoy-Partition nach der Installation sind ohne echten Testlauf nicht mit Sicherheit
  vorhersagbar — beides wird best-effort implementiert und im Test korrigiert.

## Architektur

```
Linux/
  LinuxUsbService.cs         (lsblk-Enumeration, injizierbarer Prozess-Runner-Delegate)
  VentoyInstallService.cs    (Download+Entpacken+pkexec-Aufruf, injizierbarer Runner-Delegate)
  ViewModels/
    LinuxMainViewModel.cs    (erweitert: Drives-Collection, Ventoy-Install-Command,
                               Copy-to-Stick-Command, Bestätigungs-Zustand)
  Views/
    MainWindow.axaml         (erweitert: Geräte-Auswahl, Secure-Boot-Checkbox,
                               Inline-Bestätigungsleiste)
```

`Core/Services/UsbService.cs` bleibt unverändert und wird weiterhin per `<Compile Include>` im
Linux-Projekt referenziert (bereits seit Phase 1 der Fall) — Phase 2 nutzt daraus gezielt
`IsVentoyInstalled`, `MoveToCategoryFolder`, `ScanStick`, `UpdateVentoyMenu`, `EnsureVentoyTheme`,
`DriveTotalMb`/`DriveFreeMb` für den "bestehender Ventoy-Stick"-Ablauf.

### `LinuxUsbService.cs`

```csharp
public sealed record LinuxBlockDevice(
    string DeviceNode,      // z.B. "/dev/sdb"
    string? MountPoint,     // null, wenn nicht eingehängt
    long SizeBytes,
    string Model,
    bool IsRemovable);

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
        return ParseLsblkJson(json); // reine, testbare Parse-Funktion, kein Prozessaufruf
    }

    internal static List<LinuxBlockDevice> ParseLsblkJson(string json) { /* … */ }

    private static async Task<string> DefaultRunLsblk()
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            "lsblk", "-J -b -o NAME,SIZE,MODEL,RM,TYPE,MOUNTPOINT")
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        proc.WaitForExit(5000);
        return output;
    }
}
```

`ParseLsblkJson` filtert auf `RM == "1"` (removable) und `TYPE == "disk"` (nicht einzelne
Partitionen) — die konkrete `lsblk -J`-Feldstruktur (verschachtelte `children`-Arrays pro
Partition) wird im Implementierungsplan anhand einer echten Beispiel-Ausgabe final festgelegt.

### `VentoyInstallService.cs`

```csharp
public sealed class VentoyInstallService
{
    public delegate Task<(int ExitCode, string Output)> RunElevatedFunc(
        string command, string args, Action<string>? onOutputLine, CancellationToken token);

    private readonly RunElevatedFunc _runElevated;

    public VentoyInstallService(RunElevatedFunc? runElevated = null)
    {
        _runElevated = runElevated ?? DefaultRunElevated;
    }

    public async Task<bool> InstallOrUpdateAsync(
        string deviceNode, bool updateMode, bool secureBoot,
        Action<string>? onLog, CancellationToken token)
    {
        string scriptPath = await EnsureVentoyScriptAsync(onLog, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(scriptPath)) return false;

        string flags = updateMode ? "-u" : secureBoot ? "-i -s" : "-i";
        var (exitCode, _) = await _runElevated(
            "pkexec", $"bash \"{scriptPath}\" {flags} {deviceNode}", onLog, token)
            .ConfigureAwait(false);
        return exitCode == 0;
    }

    private static Task<(int, string)> DefaultRunElevated(
        string command, string args, Action<string>? onOutputLine, CancellationToken token)
    { /* pkexec-Prozessaufruf, Stdout zeilenweise an onOutputLine */ }

    private async Task<string> EnsureVentoyScriptAsync(Action<string>? onLog, CancellationToken token)
    { /* GitHub-Release-Auflösung (Linux-Asset statt Windows-Asset), Download, .tar.gz entpacken
         via System.Formats.Tar (Teil von .NET 8, kein neues NuGet-Paket) */ }
}
```

### `LinuxMainViewModel` (Erweiterung)

Neue Properties/Commands: `Drives` (`ObservableCollection<LinuxBlockDevice>`), `SelectedDrive`,
`SecureBootEnabled` (bool), `PendingConfirmation` (string? — gesetzte Warnmeldung schaltet die
Inline-Bestätigungsleiste sichtbar), `ConfirmVentoyCommand`/`CancelVentoyCommand`,
`CopyToStickCommand`. Drive-Polling über einen Avalonia-`DispatcherTimer` (8 Sekunden, analog zum
Windows-Intervall), ruft `LinuxUsbService.ListRemovableDevicesAsync()` und aktualisiert `Drives`.

## Datenfluss

**Kopieren auf bestehenden Ventoy-Stick:**
1. Polling erkennt Gerät mit `MountPoint != null` und `UsbService.IsVentoyInstalled(mountPoint)`.
2. Nutzer wählt Gerät + ISO(s), klickt "Auf Stick kopieren".
3. Datei(en) werden in den passenden Kategorie-Ordner kopiert (`UsbService.MoveToCategoryFolder`
   bzw. direkte Kopie analog zum bestehenden `CopyToUsbWorker`-Muster), danach
   `UsbService.UpdateVentoyMenu(mountPoint, entries)` zur Aktualisierung des Bootmenüs.

**Ventoy auf frischem/bestehendem Stick installieren/aktualisieren:**
1. Nutzer wählt ein Gerät aus `Drives`, klickt "Ventoy einrichten" (Install) oder "Ventoy
   aktualisieren" (Update, falls `IsVentoyInstalled` bereits `true`).
2. `PendingConfirmation` wird gesetzt (Gerätepfad + Größe + Warntext "ALLE DATEN WERDEN GELÖSCHT"
   bei Install), Inline-Leiste erscheint.
3. Nutzer bestätigt → `VentoyInstallService.InstallOrUpdateAsync(...)` — `pkexec`-Passwort-Dialog
   erscheint (vom System, nicht von ULM gezeichnet), danach läuft `Ventoy2Disk.sh` mit
   Fortschritts-Log-Zeilen in der Status-Anzeige.
4. Nach Erfolg: kurze Wartezeit + erneutes `lsblk`-Polling, bis die neue Ventoy-Partition
   eingehängt ist, dann `UsbService.EnsureVentoyTheme(neuerMountPoint)`.

## Fehlerbehandlung

`pkexec`-Abbruch (Passwort-Dialog weggeklickt) liefert einen Nicht-Null-Exitcode → wie jeder
andere Fehlschlag behandelt (Statuszeile zeigt Fehlschlag, kein Absturz). Netzwerkfehler beim
Ventoy-Release-Download → gleiches Muster wie bestehende Download-Fehlerbehandlung. Es wird nie
automatisch ein Gerät vorausgewählt; die Bestätigungsleiste zeigt immer den vollen Gerätepfad
sowie die Größe, damit ein Erkennungsfehler aus der `lsblk`-Auswertung vor einer destruktiven
Aktion auffallen kann.

## Testing

`RunLsblkFunc` und `RunElevatedFunc` sind injizierbare Delegates (gleiches Muster wie
`DownloadFunc`/`ResolveFunc` aus Phase 1) — Unit-Tests für `ParseLsblkJson` laufen gegen
Fixture-JSON-Strings, Tests für `VentoyInstallService` simulieren Exitcodes/Output ohne echten
`pkexec`-Aufruf. Kein Test ruft echte Hardware oder Root-Rechte auf.

## Manuelle Verifikation

Ausschließlich durch den Nutzer auf echtem Linux Mint mit echtem USB-Stick:
1. Bereits eingerichteten Ventoy-Stick einstecken, prüfen ob er in der Liste erscheint und ISOs
   sich kopieren lassen (Bootmenü danach auf einem zweiten Gerät oder per Reboot prüfen).
2. Frischen/leeren USB-Stick einstecken, "Ventoy einrichten" auslösen, `pkexec`-Passwort-Dialog
   bestätigen, Ergebnis abwarten — hier ist am ehesten mit Anpassungsbedarf zu rechnen (exakte
   `Ventoy2Disk.sh`-Ausgabeform, Mount-Timing danach).
3. Nach erfolgreicher Installation: Stick erneut einstecken/prüfen, ob das ULM-Theme/Bootmenü
   korrekt erscheint.

## Offene Fragen für spätere Iterationen (nicht jetzt entscheiden)

- Ob ein `.policy`-Polkit-Profil sinnvoll ist, um wiederholte Passwort-Eingaben zu vermeiden
  (optionale Politur, nicht Teil dieser Phase).
- Verhalten bei mehreren gleichzeitig angeschlossenen Wechseldatenträgern (Windows fragt aktiv
  nach — für Linux zunächst dieselbe Logik übernehmen, sofern sich beim Test kein Unterschied
  zeigt).
