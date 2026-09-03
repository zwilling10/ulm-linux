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
        public delegate Task UnmountFunc(string deviceNode, Action<string>? onLog, CancellationToken token);

        private readonly FetchLatestUrlFunc _fetchLatestUrl;
        private readonly DownloadFunc _download;
        private readonly RunElevatedFunc _runElevated;
        private readonly UnmountFunc _unmount;
        private readonly string _cacheDir;

        public VentoyInstallService(
            FetchLatestUrlFunc? fetchLatestUrl = null, DownloadFunc? download = null,
            RunElevatedFunc? runElevated = null, string? cacheDir = null, UnmountFunc? unmount = null)
        {
            _fetchLatestUrl = fetchLatestUrl ?? DefaultFetchLatestUrlAsync;
            _download = download ?? DefaultDownloadAsync;
            _runElevated = runElevated ?? DefaultRunElevatedAsync;
            _unmount = unmount ?? DefaultUnmountAllPartitionsAsync;
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

        public async Task<bool> InstallOrUpdateAsync(
            string deviceNode, bool updateMode, bool secureBoot, Action<string>? onLog, CancellationToken token)
        {
            // Nutzerfund auf echter Hardware (erster echter Test dieses Pfads — laut Projekt-
            // Historie bisher nur über WSL cross-kompiliert, nie ausgeführt): Linux Mint haengt
            // neu eingesteckte Sticks per udisks2 automatisch ein (uid=1000-Mount, siehe
            // Log-Ausschnitt). Ventoy2Disk.sh bricht darauf sofort mit "already mounted, please
            // umount it first!" ab. Vorher unprivilegiert aushaengen (udisksctl unmount — der
            // einloggte Nutzer besitzt den eigenen udisks2-Auto-Mount, braucht dafür KEIN pkexec).
            // Reine Best-Effort-Schleife über die üblichen Partitionsnummern; ein Fehlschlag
            // (Partition existiert nicht / war eh nicht gemountet) wird bewusst verschluckt.
            await _unmount(deviceNode, onLog, token).ConfigureAwait(false);

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

        /// <summary>Hängt das Ganze-Laufwerk selbst sowie Partitionen 1–4 aus (übliche Ventoy-
        /// Stick-Struktur: max. 2–3 Partitionen; die Schleife deckt großzügig ab). Unprivilegiert
        /// via `udisksctl unmount` (ArgumentList — kein Shell-Escaping nötig, deviceNode kommt vom
        /// Kernel/lsblk und enthält keine Sonderzeichen). Ein Fehlschlag pro Versuch (Partition
        /// existiert nicht, war nicht gemountet, o.ä.) ist erwartet und wird verschluckt — nur bei
        /// tatsächlichem Erfolg wird geloggt.</summary>
        private static async Task DefaultUnmountAllPartitionsAsync(string deviceNode, Action<string>? onLog, CancellationToken token)
        {
            var candidates = new System.Collections.Generic.List<string> { deviceNode };
            for (int i = 1; i <= 4; i++) candidates.Add(deviceNode + i);

            foreach (string candidate in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo("udisksctl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                    psi.ArgumentList.Add("unmount");
                    psi.ArgumentList.Add("-b");
                    psi.ArgumentList.Add(candidate);
                    using var proc = Process.Start(psi);
                    if (proc is null) continue;
                    await proc.WaitForExitAsync(token).ConfigureAwait(false);
                    if (proc.ExitCode == 0) onLog?.Invoke($"Ausgehängt: {candidate}");
                }
                catch { /* udisksctl evtl. nicht installiert, oder candidate existiert nicht — best effort */ }
            }
        }

        private static async Task<(int, string)> DefaultRunElevatedAsync(string command, string args, Action<string>? onOutputLine, CancellationToken token)
        {
            var psi = new ProcessStartInfo(command, args)
            { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null) return (-1, string.Empty);

            var stdoutBuilder = new System.Text.StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdoutBuilder.AppendLine(e.Data); onOutputLine?.Invoke(e.Data); } };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) { stdoutBuilder.AppendLine(e.Data); onOutputLine?.Invoke(e.Data); } };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Nutzerfund: Ventoy2Disk.sh fragt vor dem eigentlichen destruktiven Schritt nochmal
            // interaktiv "y/n" auf stdin ab. Ohne Terminal (Aufruf über pkexec aus einer GUI-App
            // heraus) bekommt das darin enthaltene "read" sofort EOF, Ventoy bricht daraufhin
            // still ab - Exitcode 0, aber KEINE Partition wird angelegt (App meldete faelschlich
            // "erfolgreich"). Die eigentliche Bestaetigung ist bereits vorher ueber ULMs eigene
            // Inline-Warnleiste erfolgt, daher hier automatisch "y" beantworten statt den Nutzer
            // ein zweites Mal (diesmal in einem unsichtbaren Konsolen-Prompt) zu fragen.
            try
            {
                await proc.StandardInput.WriteLineAsync("y").ConfigureAwait(false);
                await proc.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch { /* Prozess evtl. bereits beendet oder erwartet gar keine Eingabe - kein Fehlerfall */ }

            await proc.WaitForExitAsync(token).ConfigureAwait(false);
            return (proc.ExitCode, stdoutBuilder.ToString());
        }
    }
}
