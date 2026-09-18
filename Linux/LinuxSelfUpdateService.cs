using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ULM.Core.Models;
using ULM.Core.Services;

namespace ULM.Linux
{
    public sealed record LinuxUpdateInfo(bool HasUpdate, string LatestVersion, string ReleaseUrl, string DownloadUrl, string? Sha256)
    {
        public static readonly LinuxUpdateInfo None = new(false, string.Empty, string.Empty, string.Empty, null);
    }

    /// <summary>Nutzerwunsch (2026-09-04, 3/3): "Automatisches Selbst-Update auch prüfen" —
    /// existierte auf Linux gar nicht (Core/Services/SelfUpdateService.cs ist komplett
    /// Windows-spezifisch: prüft auf einen Inno-Setup-Deinstaller, ersetzt die laufende .exe über
    /// ein Batch-Skript, weil Windows eine offene .exe-Datei nicht direkt überschreiben lässt).
    /// Bewusst eigenständig statt Wiederverwendung von SelfUpdateService/UlmUpdateInfo/
    /// MatchUlmReleaseAssets — diese sind Windows-getestet (ULM.Tests/HttpServiceTests.cs/
    /// SelfUpdateServiceTests.cs) und von hier aus nicht baubar/verifizierbar; ein rein additiver
    /// Griff in dieselben Dateien wäre vermeidbares Risiko für ungetestet bleibenden, aber
    /// produktiv genutzten Windows-Code. Nutzt stattdessen nur die bereits plattformneutralen,
    /// öffentlichen Bausteine direkt (HttpService.GetStringAsync/DownloadAsync/IsVersionNewer,
    /// IsoEntry.ComputeSha256Async). Auf Linux ist "ersetzen" simpler als unter Windows: eine
    /// laufende Binärdatei kann per POSIX-rename() direkt überschrieben werden (der laufende
    /// Prozess behält sein offenes Inode, kein Neustart-Skript nötig).</summary>
    public sealed class LinuxSelfUpdateService
    {
        private static readonly Lazy<LinuxSelfUpdateService> _lazy = new(() => new LinuxSelfUpdateService());
        public static LinuxSelfUpdateService Instance => _lazy.Value;
        private LinuxSelfUpdateService() { }

        /// <summary>Sucht im neuesten GitHub-Release ein Asset, dessen Name mit "ulm-linux-" beginnt
        /// und auf "-x64" endet (siehe .github/workflows/release.yml im ulm-linux-Repo, Muster
        /// "ulm-linux-v{VERSION}-x64") — fehlt es (älteres Release vor Einführung dieses Features),
        /// liefert HasUpdate=false statt eine nicht herunterladbare Version zu melden.</summary>
        public async Task<LinuxUpdateInfo> CheckForUpdateAsync(string currentVersion, string repo = "zwilling10/ulm-linux")
        {
            try
            {
                string? json = await HttpService.Instance.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest").ConfigureAwait(false);
                if (json is null) return LinuxUpdateInfo.None;
                using var doc = JsonDocument.Parse(json);
                string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                string releaseUrl = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                string latest = tag.TrimStart('v', 'V');
                if (string.IsNullOrWhiteSpace(latest)) return LinuxUpdateInfo.None;

                var assetList = new List<(string Name, string Url)>();
                string downloadUrl = string.Empty;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                    {
                        string n = a.TryGetProperty("name", out var nn) ? nn.GetString() ?? string.Empty : string.Empty;
                        string au = a.TryGetProperty("browser_download_url", out var uu) ? uu.GetString() ?? string.Empty : string.Empty;
                        assetList.Add((n, au));
                        if (downloadUrl.Length == 0 && n.StartsWith("ulm-linux-", StringComparison.OrdinalIgnoreCase) && n.EndsWith("-x64", StringComparison.OrdinalIgnoreCase))
                            downloadUrl = au;
                    }
                if (string.IsNullOrEmpty(downloadUrl)) return LinuxUpdateInfo.None;

                string? sha256 = null;
                string sumsUrl = assetList.FirstOrDefault(a => string.Equals(a.Name, "SHA256SUMS", StringComparison.OrdinalIgnoreCase)).Url;
                if (!string.IsNullOrEmpty(sumsUrl))
                {
                    string? sums = await HttpService.Instance.GetStringAsync(sumsUrl).ConfigureAwait(false);
                    if (sums is not null) sha256 = HttpService.ParseSha256SumsLine(sums, Path.GetFileName(new Uri(downloadUrl).AbsolutePath));
                }

                return new LinuxUpdateInfo(HttpService.IsVersionNewer(latest, currentVersion), latest, releaseUrl, downloadUrl, sha256);
            }
            catch { return LinuxUpdateInfo.None; }
        }

        /// <summary>Lädt die neue Binärdatei in ein Temp-Verzeichnis; verifiziert bei vorhandenem
        /// SHA256SUMS-Eintrag den Hash (fail-open ohne Hash, wie unter Windows) und löscht die
        /// Datei bei Mismatch statt eine beschädigte/manipulierte Version anzuwenden. Liefert null
        /// bei jedem Fehlschlag.</summary>
        public async Task<string?> DownloadUpdateAsync(LinuxUpdateInfo info, string tempDir, IProgress<(int Percent, string Detail)>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(tempDir);
            string dest = Path.Combine(tempDir, $"ulm-linux-update-{info.LatestVersion}");
            bool ok = await HttpService.Instance.DownloadAsync(info.DownloadUrl, dest, progress, ct).ConfigureAwait(false);
            if (!ok) return null;

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                string actual = await IsoEntry.ComputeSha256Async(dest, ct).ConfigureAwait(false);
                if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(dest); } catch { /* best effort */ }
                    return null;
                }
            }
            return dest;
        }

        /// <summary>Ersetzt die laufende Binärdatei und startet neu. Auf Linux (anders als Windows)
        /// braucht das kein Skript/keinen Neustart-Helferprozess: rename() über eine offene,
        /// gerade ausgeführte Datei ist ein normaler, atomarer POSIX-Vorgang — der laufende Prozess
        /// behält sein Inode bis zum Exit, der neue Prozess lädt danach die neue Datei vom selben
        /// Pfad. Wirft bei fehlenden Schreibrechten am Zielpfad (der Aufrufer fängt das ab und zeigt
        /// eine Fehlermeldung statt eines Absturzes).</summary>
        public void ApplyUpdateAndRestart(string downloadedFilePath, string currentExePath)
        {
            // CA1416: File.SetUnixFileMode ist plattformspezifisch — dieses gesamte Projekt ist
            // per RuntimeIdentifier fest auf linux-x64 gepinnt (ULM.Linux.csproj), läuft also nie
            // unter Windows; die Warnung ist ein bewusst hingenommener Fehlalarm des Analyzers.
#pragma warning disable CA1416
            File.SetUnixFileMode(downloadedFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
            File.Move(downloadedFilePath, currentExePath, overwrite: true);
            Process.Start(currentExePath);
            Environment.Exit(0);
        }
    }
}
