using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.ViewModels;

namespace ULM.Linux.ViewModels
{
    public sealed record CategoryOption(string? Key, string Label);

    public sealed class LinuxMainViewModel : ViewModelBase
    {
        public delegate Task<bool> DownloadFunc(string url, string destPath, IProgress<(int Percent, string Detail)>? progress, CancellationToken token);
        public delegate Task<(string Version, string Url, string Filename)> ResolveFunc(IsoEntry entry);

        private readonly IIsoDatabaseService _db;
        private readonly string _downloadDirectory;
        private readonly string _settingsIniPath;
        private readonly DownloadFunc _download;
        private readonly ResolveFunc _resolve;
        private readonly LinuxUsbService _usbService;
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

        public ObservableCollection<CategoryOption> Categories { get; }
        public ObservableCollection<LinuxIsoRow> Rows { get; }

        private CategoryOption? _selectedCategory;
        public CategoryOption? SelectedCategory
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
            set { if (SetField(ref _selectedRow, value)) RaiseAllCommandsCanExecuteChanged(); }
        }

        public ObservableCollection<LinuxBlockDevice> Drives { get; }

        private LinuxBlockDevice? _selectedDrive;
        public LinuxBlockDevice? SelectedDrive
        {
            get => _selectedDrive;
            set { if (SetField(ref _selectedDrive, value)) RaiseAllCommandsCanExecuteChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetField(ref _isBusy, value)) RaiseAllCommandsCanExecuteChanged(); }
        }

        // SelectedRow/SelectedDrive/IsBusy sind gemeinsame Vorbedingungen fuer mehrere Commands
        // (z.B. haengt CopyToStickCommand von SelectedRow UND SelectedDrive UND !IsBusy ab).
        // Nutzerfund: SelectedDrive.set rief bisher GAR KEIN RaiseCanExecuteChanged auf - nach
        // Geraeteauswahl blieben "Ventoy einrichten"/"Ventoy aktualisieren"/"Auf Stick kopieren"
        // dauerhaft ausgegraut, obwohl CanExecute() bei tatsaechlicher Abfrage schon true
        // zurueckgegeben haette (Avalonia fragt CanExecute aber nur ab, wenn CanExecuteChanged
        // feuert). Ein zentraler Sammel-Aufruf statt einzeln nachverfolgter Abhaengigkeiten
        // verhindert, dass dieselbe Luecke bei einem kuenftigen neuen Command wieder entsteht.
        private void RaiseAllCommandsCanExecuteChanged()
        {
            DownloadCommand.RaiseCanExecuteChanged();
            CopyToStickCommand.RaiseCanExecuteChanged();
            RequestVentoyInstallCommand.RaiseCanExecuteChanged();
            RequestVentoyUpdateCommand.RaiseCanExecuteChanged();
            ConfirmVentoyCommand.RaiseCanExecuteChanged();
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

        private string _copyStatus = string.Empty;
        public string CopyStatus
        {
            get => _copyStatus;
            private set => SetField(ref _copyStatus, value);
        }

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

        public string LanguageButtonLabel => LocalizationService.Current == AppLanguage.German ? "EN" : "DE";
        public string SearchPlaceholder   => LocalizationService.T(Str.Linux_Toolbar_SearchPlaceholder);
        public string RefreshLabel        => LocalizationService.T(Str.Linux_Toolbar_Refresh);

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ToggleLanguageCommand { get; }
        public RelayCommand DownloadCommand { get; }
        public RelayCommand CopyToStickCommand { get; }
        public RelayCommand RequestVentoyInstallCommand { get; }
        public RelayCommand RequestVentoyUpdateCommand { get; }
        public RelayCommand ConfirmVentoyCommand { get; }
        public RelayCommand CancelVentoyCommand { get; }

        public void Refresh()
        {
            _db.Load();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Rows.Clear();
            IEnumerable<IsoEntry> entries = _db.Entries;
            if (SelectedCategory?.Key is not null)
                entries = entries.Where(e => e.Category == SelectedCategory.Key);
            if (!string.IsNullOrWhiteSpace(SearchText))
                entries = entries.Where(e => e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            foreach (IsoEntry entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new LinuxIsoRow(entry, _downloadDirectory));
        }

        private void RebuildCategories()
        {
            // previousKey ist auch beim allerersten Aufruf (Konstruktor, _selectedCategory noch
            // null) und bei ausgewähltem "Alle"/"All" korrekt null — Categories.First(...) trifft
            // in beiden Fällen den frisch angelegten "Alle/All"-Eintrag (Key null, zuerst
            // hinzugefügt). Ohne dieses einheitliche Vorgehen blieb _selectedCategory nach einem
            // Sprachwechsel bei "Alle" ausgewählt auf dem alten, nicht mehr in Categories
            // enthaltenen Objekt stehen.
            string? previousKey = _selectedCategory?.Key;
            Categories.Clear();
            Categories.Add(new CategoryOption(null, LocalizationService.T(Str.Linux_Category_All)));
            foreach (string key in Constants.Categories)
                Categories.Add(new CategoryOption(key, Constants.CategoryLabel(key)));
            _selectedCategory = Categories.First(c => c.Key == previousKey);
        }

        public async Task DownloadSelectedAsync()
        {
            if (SelectedRow is null || IsBusy) return;
            IsoEntry entry = SelectedRow.Entry;

            IsBusy = true;
            DownloadPercent = 0;
            DownloadStatus = string.Empty;
            try
            {
                // Viele Katalog-Einträge haben keine feste Url/Mirror-Konfiguration, sondern werden
                // erst hier zur Laufzeit aufgelöst (GitHub-Releases, versionsabhängige Downloadseiten
                // — siehe Core/Services/HttpService.DistroResolvers.cs). Ohne diesen Schritt liefert
                // AllDownloadUrls() für die meisten Einträge nichts (Nutzerfund: "Keine Download-URL
                // hinterlegt" bei praktisch jeder Distro). Gleiches Muster wie DownloadWorker in
                // Core/Workers/Workers.cs.
                var (version, resolvedUrl, resolvedFilename) = await _resolve(entry).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(version)) entry.RemoteVersion = version;
                if (!string.IsNullOrWhiteSpace(resolvedFilename)) entry.Filename = resolvedFilename;

                var urls = entry.AllDownloadUrls(resolvedUrl).ToList();
                if (urls.Count == 0)
                {
                    DownloadStatus = LocalizationService.T(Str.Linux_Download_NoUrl);
                    return;
                }

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
            // Bewusst leeren statt nur die letzte Zeile zu ueberschreiben (wie zuvor) — sonst geht
            // die eigentliche Ventoy2Disk.sh-Ausgabe verloren, genau dann wenn man sie fuer die
            // Fehlerdiagnose am meisten braucht (Nutzerfund: App zeigte nur "Erfolgreich", obwohl
            // im Hintergrund nichts passiert war).
            VentoyStatus = string.Empty;
            try
            {
                bool ok = await _ventoyInstallService.InstallOrUpdateAsync(
                    deviceNode, updateMode, secureBoot,
                    onLog: line => VentoyStatus = string.IsNullOrEmpty(VentoyStatus) ? line : VentoyStatus + "\n" + line,
                    CancellationToken.None).ConfigureAwait(true);
                string result = ok ? "Ventoy-Vorgang abgeschlossen." : "Ventoy-Vorgang fehlgeschlagen.";
                VentoyStatus = string.IsNullOrEmpty(VentoyStatus) ? result : VentoyStatus + "\n" + result;
            }
            finally
            {
                IsBusy = false;
            }
        }

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
    }
}
