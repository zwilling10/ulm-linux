# Universal Linux Manager (ULM)

Portabler Manager für Windows und Linux für Ventoy-Multiboot-USB-Sticks mit Linux-Live-ISOs. Lädt aktuelle Versionen automatisch von den offiziellen Quellen, kopiert sie auf den Stick und hält den ganzen Katalog dauerhaft aktuell — auch ohne hinterlegte URLs.

🔗 **[Projektseite & Download](https://zwilling10.github.io/ULM/)** · **[Neueste Version](../../releases/latest)**

## Funktionen

- **Automatische ISO-Downloads** — über 20 dedizierte Erkennungsroutinen lösen für jede unterstützte Distro immer die aktuellste Download-URL direkt beim Anbieter auf, keine hartkodierten Links
- **Ventoy-Integration** — kopiert Downloads direkt auf den Stick, aktualisiert das Bootmenü automatisch
- **Gesundheitscheck & Duplikat-Schutz** — läuft automatisch nach jedem Download oder Scan
- **Datenmüll-Schutz** — Online-Größenprüfung erkennt unvollständige Downloads zuverlässig
- **Selbstlernende Erkennung** — für JEDE unbekannte/importierte Distro löst ULM die Quelle automatisch auf (dedizierte Erkenner → DistroWatch-Suche → SourceForge → Websuche) und merkt sie sich dauerhaft
- **Download-Fortschritt mit ETA** — Geschwindigkeit, verbleibende Zeit und übertragene Menge in Echtzeit
- **Freispeicher-Check** — prüft vor Download/Kopieren, ob genug Platz am Ziel vorhanden ist
- **Selbst-Update-Check** — meldet im Protokoll, wenn eine neuere ULM-Version verfügbar ist, inkl. „Was ist neu?“-Dialog nach einem Update
- **Log-Rotation** — die Protokolldatei wird ab 5 MB automatisch rotiert statt unbegrenzt zu wachsen
- **Dark Mode** — Design-Wahl System/Hell/Dunkel, schaltet live um (kein Neustart nötig), folgt bei "System" automatisch der Windows-Einstellung
- **Autostart-Option** — optionale Checkbox im Einrichtungsfenster startet ULM automatisch mit Windows, kein Admin-Recht nötig
- **Portabel oder installiert** — wahlweise eine einzige self-contained .exe ohne Installation, oder ein klassischer Setup.exe mit Startmenü-Eintrag und Deinstaller; keine .NET-Installation auf dem Zielsystem nötig

## Download

Unter [Releases](../../releases/latest) stehen Varianten für Windows und Linux bereit:

**Windows:**
- **Portable `.exe`** (`UniversalLinuxManager-vX.Y.Z-win-x64.exe`) — einfach herunterladen und starten, keine Installation, keine Administratorrechte nötig (außer für die optionale Ventoy-Installation/-Aktualisierung)
- **Setup `.exe`** (`UniversalLinuxManager-Setup-vX.Y.Z-win-x64.exe`) — klassischer Installer mit Startmenü-Eintrag, optionalem Desktop-Icon und Deinstaller unter "Programme und Features"

**Linux:**
- **Portable Binärdatei** (`UniversalLinuxManager-vX.Y.Z-linux-x64`) — herunterladen, mit `chmod +x` ausführbar machen und starten, keine Paketinstallation nötig (außer für die optionale Ventoy-Installation/-Aktualisierung, per `pkexec`)

**Anforderungen:** Windows 10 / 11 (x64) oder Linux x86_64 (jede aktuelle Distribution)

## Aus dem Quellcode bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/zwilling10/ULM.git
cd ULM
./build-release.sh                        # baut portable Single-File-Binaries für Windows UND Linux nach release/
./build-release.sh --zip                  # Windows-EXE zusätzlich als .zip verpackt
./build-release.sh --installer            # zusätzlich klassischen Windows-Setup.exe bauen (benötigt Inno Setup, https://jrsoftware.org/isdl.php)
```

Oder direkt mit `dotnet publish`:

```bash
dotnet publish UniversalLinuxManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish Linux/ULM.Linux.csproj -c Release -r linux-x64 --self-contained true
```

## Tests

```bash
dotnet test ULM.Tests/ULM.Tests.csproj                              # Windows-Testsuite (nur unter Windows/net8.0-windows baubar)
dotnet test Linux/ULM.Linux.Tests/ULM.Linux.Tests.csproj            # Linux-Testsuite
```

Läuft bei jedem Push automatisch per GitHub Actions CI.

## Architektur

MVVM (.NET 8), zwei eigenständige UI-Projekte über gemeinsamem `Core/`:

- `Core/Models` — Domänenmodell (`IsoEntry`, `UsbDrive`, Konstanten)
- `Core/Services` — `HttpService` (URL-Auflösung/Downloads), `UsbService` (Laufwerks-/Ventoy-Verwaltung), `IsoDatabaseService` (INI-Persistenz)
- `Core/Workers` — Hintergrund-Worker für Downloads, Scans, Versionschecks
- `ViewModels` / `Views` — WPF-Oberfläche für Windows (MVVM-Bindung, Dialoge)
- `Linux/` — eigenständiges Avalonia-Projekt für Linux, nutzt `Core/`/`Infrastructure` unverändert mit

## Lizenz

[MIT](LICENSE) © 2025 ULM Project
