using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;

namespace ULM.Linux.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContextChanged += (_, _) => WireViewModel();
        }

        private LinuxMainViewModel? _vm;
        private DownloadProgressDialog? _downloadProgressDialog;
        private bool _orphanCheckDone;

        private void WireViewModel()
        {
            if (_vm is not null) { _vm.HealthCheckCompleted -= OnHealthCheckCompleted; _vm.AutoVersionCheckCompleted -= OnAutoVersionCheckCompleted; }
            _vm = DataContext as LinuxMainViewModel;
            if (_vm is not null)
            {
                _vm.HealthCheckCompleted += OnHealthCheckCompleted;
                // Windows-Pendant: MainWindow.xaml.cs' `_vm.AutoVersionCheckCompleted += async () =>
                // { ... await RunLocalFileMaintenanceAsync(); }` — läuft genau einmal pro Sitzung,
                // direkt nach dem ersten abgeschlossenen Online-Versionscheck ("Datenmüll-Schutz",
                // Nutzerwunsch 2026-09-04).
                _vm.AutoVersionCheckCompleted += OnAutoVersionCheckCompleted;
                // Windows-Pendant: MainWindow.xaml.cs' `_vm.ConfirmSlowDownload = (name, host) =>
                // MessageBox.Show(...)`. DownloadWorker ruft das synchron von einem Hintergrund-
                // Thread (dem jeweils langsamen Download-Slot) auf — InvokeAsync(Func<Task<bool>>)
                // marschalliert Öffnen+Warten auf den UI-Thread, GetAwaiter().GetResult() blockiert
                // dabei nur den aufrufenden Slot-Thread, nicht den UI-Thread selbst (der pumpt die
                // Dialog-Nachrichtenschleife währenddessen normal weiter).
                _vm.ConfirmSlowDownload = (name, host) =>
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmDialog.ShowAsync(this,
                        LocalizationService.T(Str.Msg_SlowDownload_Title),
                        string.Format(LocalizationService.T(Str.Msg_SlowDownload_Body), name, host)))
                    .GetAwaiter().GetResult();

                // Windows-Pendant: MainWindow.xaml.cs' DownloadItemProgress/DownloadBatchCompleted/
                // CopyItemProgress/CopyBatchCompleted-Verdrahtung — einmalig abonniert, reicht nur
                // durch, wenn _downloadProgressDialog gerade offen ist (OpenProgressDialog).
                _vm.DownloadItemProgress += (name, pct, status, canFaster) => _downloadProgressDialog?.UpdateDownload(name, pct, status, canFaster);
                _vm.CopyItemProgress     += (name, pct, status) => _downloadProgressDialog?.UpdateCopy(name, pct, status);
                _vm.DownloadBatchCompleted += (ok, failed) => _downloadProgressDialog?.SetOverallComplete(
                    string.Format(LocalizationService.T(Str.Log_DownloadedCountStatus), ok, ok + failed));
                // Läuft im Kopier-Modus NACH DownloadBatchCompleted und überschreibt dessen
                // Zusammenfassung mit dem endgültigen Ergebnis — Windows-Pendant macht dasselbe
                // (dort inline über MainWindow.xaml.cs, hier über dieselben geteilten Log_*-Werte
                // wie StartCopyToStick/CopySelectedToStickAsync statt hartcodiertem Text).
                _vm.CopyBatchCompleted += count => _downloadProgressDialog?.SetOverallComplete(
                    count > 0 ? string.Format(LocalizationService.T(Str.Log_CopiedToStickStatus), count, _vm.SelectedDrive?.MountPoint)
                              : LocalizationService.T(Str.Log_NothingToCopyStatus));
            }
        }

        private void OpenProgressDialog(IEnumerable<string> names, bool hasDownload, bool hasCopy)
        {
            _downloadProgressDialog?.Close();
            var dlg = new DownloadProgressDialog(names, hasDownload, hasCopy);
            dlg.CancelRequested += () => _vm?.CancelDownloadCommand.Execute(null);
            dlg.FasterMirrorRequested += name => _vm?.RequestFasterMirrorCommand.Execute(name);
            dlg.Closed += (_, _) => _downloadProgressDialog = null;
            _downloadProgressDialog = dlg;
            dlg.Show(this);
        }

        // Bewusst Code-behind statt reines MVVM-Command (gleiches Muster wie Windows'
        // MainWindow.xaml.cs BtnGitHubToken_Click/HealthCheckCompleted-Handler) — Dialoge brauchen
        // ein Fenster als Owner, das gehört in die View, nicht ins ViewModel.
        private async void BtnGitHubToken_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            var dlg = new GitHubTokenDialog(_vm.GitHubToken);
            string? result = await dlg.ShowDialog<string?>(this);
            if (result is not null) _vm.GitHubToken = result;
        }

        private async void BtnSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            await new SettingsDialog(_vm).ShowDialog(this);
        }

        private async void BtnHelp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            await new HelpDialog().ShowDialog(this);

        // DB-Gesundheitscheck selbst läuft komplett über RunHealthCheckCommand (MVVM-Binding in
        // MainWindow.axaml, braucht kein Fenster als Owner) — nur das Ergebnis-Dialog-Öffnen
        // danach braucht Code-behind (Owner-Fenster).
        private void OnHealthCheckCompleted(System.Collections.Generic.IReadOnlyList<ULM.Core.Workers.VersionCheckEntryResult> results)
        {
            var dlg = new DbHealthCheckDialog(results);
            _ = dlg.ShowDialog(this);
        }

        private async void OnAutoVersionCheckCompleted()
        {
            if (_orphanCheckDone) return;
            _orphanCheckDone = true;
            await RunLocalFileMaintenanceAsync();
        }

        /// <summary>Windows-Pendant: MainWindow.xaml.cs RunLocalFileMaintenanceAsync — "Datenmüll-
        /// Schutz" (Nutzerwunsch 2026-09-04): scannt das Arbeitsverzeichnis nach .iso/.part-Dateien,
        /// klassifiziert sie (leer/verwaist/unvollständig/zu klein/ok) und bietet die verdächtigen
        /// zum Löschen an. Bewusst NICHT mit übernommen: der anschließende Windows-Zweig
        /// "GetVerifiedCompleteEntriesMissingFromStick → OnMissingOnStickDetected" (auf Stick fehlende,
        /// aber lokal vollständige ISOs automatisch zum Nachkopieren anbieten) — eigenständiges
        /// Feature, nicht Teil von "Datenmüll-Schutz", separat vormerken falls gewünscht.</summary>
        private async Task RunLocalFileMaintenanceAsync()
        {
            if (_vm is null) return;
            try
            {
                string dir = _vm.DownloadDirectory;
                if (!System.IO.Directory.Exists(dir)) { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_WorkFolderNotFound), dir)); return; }

                var dbEntries = IsoDatabaseService.Instance.Entries;
                var byFilename = new Dictionary<string, IsoEntry>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var e in dbEntries)
                    if (!string.IsNullOrWhiteSpace(e.Filename) && !byFilename.ContainsKey(e.Filename))
                        byFilename[e.Filename] = e;

                var candidates = new List<(string Path, long Size)>();
                _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_ScanningIsoFolder), dir));

                string[] isoFiles;
                try { isoFiles = System.IO.Directory.GetFiles(dir, "*.iso", System.IO.SearchOption.AllDirectories); }
                catch (System.Exception ex) { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_ScanError), ex.Message)); isoFiles = System.Array.Empty<string>(); }
                _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_IsoFilesFoundCount), isoFiles.Length));

                foreach (string f in isoFiles)
                {
                    string name = System.IO.Path.GetFileName(f);
                    long size = IsoEntry.GetRobustLength(f);

                    if (size == 0)
                    { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileEmpty), RelativePath(dir, f))); candidates.Add((f, 0)); continue; }

                    if (!byFilename.TryGetValue(name, out var entry))
                    { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileOrphaned), RelativePath(dir, f), FmtSize(size))); candidates.Add((f, size)); continue; }

                    long expected = await HttpService.Instance.GetExpectedSizeAsync(entry).ConfigureAwait(true);
                    if (expected > 0 && size < expected * 0.98)
                    { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileIncomplete), RelativePath(dir, f), FmtSize(size), FmtSize(expected))); candidates.Add((f, size)); }
                    else if (expected > 0)
                    { entry.VerifiedComplete = true; _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileComplete), RelativePath(dir, f), FmtSize(size))); }
                    else if (size < Constants.MinIsoSizeBytes)
                    { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileTooSmallUnverified), RelativePath(dir, f), FmtSize(size))); candidates.Add((f, size)); }
                    else
                    { entry.VerifiedComplete = true; _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileOkUnverified), RelativePath(dir, f), FmtSize(size))); }
                }

                try
                {
                    foreach (string f in System.IO.Directory.GetFiles(dir, "*.part", System.IO.SearchOption.AllDirectories))
                    { long size = IsoEntry.GetRobustLength(f); _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileCancelledPartial), RelativePath(dir, f), FmtSize(size))); candidates.Add((f, size)); }
                }
                catch (System.Exception ex) { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_PartSearchError), ex.Message)); }

                if (candidates.Count == 0)
                {
                    _vm.LogEntries.Add(LocalizationService.T(Str.Log_NoJunkFound));
                    return;
                }

                _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_JunkFilesClassified), candidates.Count));
                var dlg = new OrphanedDownloadsDialog(candidates);
                if (await dlg.ShowDialog<bool>(this))
                {
                    int deleted = 0, failed = 0;
                    foreach (string path in dlg.ToDelete)
                    {
                        if (IsoEntry.TryDelete(path, line => _vm.LogEntries.Add(line))) { deleted++; _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_Deleted), RelativePath(dir, path))); }
                        else failed++;
                    }
                    _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FilesDeletedSimpleStatus), deleted)
                        + (failed > 0 ? string.Format(LocalizationService.T(Str.Log_FailedSuffix), failed) : "") + ".");
                    if (deleted > 0) _vm.Refresh();
                }
                else _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_MaintenanceSkipped), candidates.Count));
            }
            catch (System.Exception ex) { _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_FileMaintenanceError), ex.Message)); }
        }

        private static string RelativePath(string root, string full) =>
            full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase) ? full[root.Length..].TrimStart('/', '\\') : full;

        private static string FmtSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return i == 0 ? $"{(long)v} B" : $"{v:F1} {units[i]}";
        }

        // Arbeitet direkt gegen IsoDatabaseService.Instance statt vm._db (siehe Plan/Windows-
        // Vorbild BtnEditDb_Click) — MoveUp/MoveDown existieren nur auf der konkreten Klasse.
        private async void BtnDatabase_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null || _vm.IsBusy) return;
            var dlg = new IsoListDialog(ULM.Core.Services.IsoDatabaseService.Instance);
            await dlg.ShowDialog<bool>(this);
            _vm.Refresh();
            if (dlg.AnyEntryAdded) _vm.RunHealthCheckCommand.Execute(null);
        }

        // Windows-Vorbild: MainWindow.xaml.cs BtnSearch_Click. Seit der Warteschlangen-Phase
        // (2026-09-04) markiert dies die übernommenen "sofort herunterladen"-Einträge nur als
        // IsSelected (angehakt) — ein Klick auf "Herunterladen" lädt sie dann als echte
        // Mehrfach-Warteschlange über DownloadQueueAsync(), kein separater Auto-Download-Zweig
        // mehr nötig wie unter Windows.
        private async void BtnSearchIso_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            var dlg = new IsoSearchDialog();
            await dlg.ShowDialog<bool>(this);
            if (dlg.AddedEntries.Count == 0) return;

            // Windows-Pendant: MainViewModel.AddImportedEntry() statt blindem Add() — "Duplikat-
            // Schutz" (Nutzerwunsch 2026-09-04): erkennt ein Online-Suchtreffer eine bereits
            // vorhandene Distro (andere Schreibweise/Dateiname), wird kein doppelter Eintrag
            // angelegt, sondern der bestehende Eintrag übernimmt ggf. den neuen Dateinamen.
            foreach (var entry in dlg.AddedEntries) _vm.AddImportedEntry(entry);
            ULM.Core.Services.IsoDatabaseService.Instance.Save();
            _vm.Refresh();
            _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_IsosAddedFromOnlineSearch), dlg.AddedEntries.Count));
            // Frisch aus der Online-Suche übernommene Einträge haben nie eine geprüfte Url — wie bei
            // Datenbank-Neuanlagen lohnt sich hier der volle Gesundheitscheck sofort.
            _vm.RunHealthCheckCommand.Execute(null);

            if (dlg.ToDownload.Count > 0)
                foreach (var row in _vm.Rows)
                    if (dlg.ToDownload.Contains(row.Entry)) row.IsSelected = true;
        }

        /// <summary>Windows-Pendant: MainWindow.xaml.cs BtnDownload_Click — Nutzerwunsch
        /// (2026-09-04): "genauso wie Windows" statt der zuvor gebauten Inline-Checkboxen/
        /// NumericUpDown-Lösung. Klärt Kopiermodus/Freispeicher/parallele Slots per Dialogkette
        /// (braucht dieses Fenster als Owner, daher Code-behind statt VM-Command — wie unter
        /// Windows) und ruft danach DownloadQueueAsync mit dem geklärten Auftrag auf.</summary>
        private async void BtnDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null || _vm.IsBusy) return;
            List<IsoEntry> queue = _vm.GetSelectedEntries();
            if (queue.Count == 0)
            {
                await InfoDialog.ShowAsync(this, Constants.AppTitle, LocalizationService.T(Str.Msg_SelectAtLeastOne));
                return;
            }

            var drive = _vm.SelectedDrive;
            string? mountPoint = drive?.MountPoint;
            bool copy = false, del = false;
            if (mountPoint is not null)
            {
                bool? mode = await ConfirmDialog.ShowYesNoCancelAsync(this,
                    LocalizationService.T(Str.Msg_DownloadMode_Title),
                    string.Format(LocalizationService.T(Str.Msg_DownloadMode_Body), mountPoint));
                if (mode is null) return;
                copy = mode == true;

                if (copy && drive?.IsVentoyInstalled != true)
                {
                    bool proceedNoVentoy = await ConfirmDialog.ShowAsync(this,
                        LocalizationService.T(Str.Msg_NoVentoy_Title),
                        string.Format(LocalizationService.T(Str.Msg_NoVentoy_Body), mountPoint));
                    if (!proceedNoVentoy) return;
                }

                if (copy)
                    del = await ConfirmDialog.ShowAsync(this,
                        LocalizationService.T(Str.Msg_DeleteFiles_Title),
                        LocalizationService.T(Str.Msg_DeleteLocalAfterCopy_AfterCopy));
            }
            else
            {
                bool proceedNoStick = await ConfirmDialog.ShowAsync(this,
                    LocalizationService.T(Str.Msg_NoStick_Title),
                    string.Format(LocalizationService.T(Str.Msg_NoStick_Body), _vm.DownloadDirectory));
                if (!proceedNoStick) return;
            }

            if (!await ConfirmEnoughFreeSpaceAsync(queue, copy ? mountPoint : null)) return;

            int slots = 1;
            if (queue.Count > 1)
            {
                int? chosen = await DownloadSlotsDialog.ShowAsync(this, queue.Count, Constants.MaxParallelSlots);
                if (chosen is null) return;
                slots = chosen.Value;
            }

            OpenProgressDialog(queue.Select(q => q.Name), hasDownload: true, hasCopy: copy);
            await _vm.DownloadQueueAsync(queue, copy ? mountPoint : null, copy, del, slots);
        }

        /// <summary>Windows-Pendant: MainWindow.xaml.cs ConfirmEnoughFreeSpaceAsync — summiert die
        /// online ermittelbare Größe aller ausgewählten Distros und vergleicht sie vorab gegen den
        /// freien Speicher am Ziel (Arbeitsverzeichnis, plus Stick falls kopiert wird), statt erst
        /// mitten im Download an fehlendem Platz zu scheitern. Best-effort: online nicht
        /// ermittelbare Größen (-1) fließen nicht in die Summe ein, blockieren den Download aber
        /// auch nicht deswegen.</summary>
        private async Task<bool> ConfirmEnoughFreeSpaceAsync(List<IsoEntry> queue, string? stickMountPoint)
        {
            long[] sizes = await Task.WhenAll(queue.Select(e => HttpService.Instance.GetExpectedSizeAsync(e))).ConfigureAwait(true);
            long totalBytes = sizes.Where(s => s > 0).Sum();
            int unknownCount = sizes.Count(s => s <= 0);
            if (totalBytes == 0) return true;

            async Task<bool> WarnIfTooSmallAsync(string label, string root)
            {
                double freeBytes;
                try
                {
                    var d = new System.IO.DriveInfo(root);
                    if (!d.IsReady) return true;
                    freeBytes = d.AvailableFreeSpace;
                }
                catch { return true; }
                if (totalBytes <= freeBytes) return true;

                static string Gb(double b) => (b / 1_073_741_824.0).ToString("F2") + " GB";
                string msg = string.Format(LocalizationService.T(Str.Msg_FreeSpace_Body1), queue.Count, Gb(totalBytes))
                    + (unknownCount > 0 ? string.Format(LocalizationService.T(Str.Msg_FreeSpace_Body2), unknownCount) : "")
                    + string.Format(LocalizationService.T(Str.Msg_FreeSpace_Body3), label, Gb(freeBytes));
                return await ConfirmDialog.ShowAsync(this, LocalizationService.T(Str.Msg_FreeSpace_Title), msg);
            }

            if (_vm is null) return true;
            if (!await WarnIfTooSmallAsync(string.Format(LocalizationService.T(Str.Msg_FreeSpace_LabelWorkDir), _vm.DownloadDirectory), _vm.DownloadDirectory))
                return false;
            if (!string.IsNullOrEmpty(stickMountPoint) &&
                !await WarnIfTooSmallAsync(string.Format(LocalizationService.T(Str.Msg_FreeSpace_LabelStick), stickMountPoint), stickMountPoint))
                return false;
            return true;
        }
    }
}
