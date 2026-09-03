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
            Groups = new ObservableCollection<CategoryGroup>();
            LogEntries = new ObservableCollection<string>();
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
            ClearLogCommand        = new RelayCommand(() => LogEntries.Clear());

            ApplyFilter();
        }

        public ObservableCollection<CategoryOption> Categories { get; }
        public ObservableCollection<LinuxIsoRow> Rows { get; }

        /// <summary>Für die ISO-Auswahl-Liste im neuen, Windows-angeglichenen Layout — Rows
        /// gruppiert nach Kategorie, immer alle Kategorien sichtbar (siehe CategoryGroup.cs).
        /// `Categories`/`SelectedCategory` bleiben unverändert bestehen (weiterhin von
        /// ApplyFilter()/RebuildCategories() genutzt und von bestehenden Tests abgedeckt) — die
        /// View bindet für die Sidebar-Auswahl künftig nicht mehr an `Categories`, sondern zeigt
        /// stattdessen `Groups`.</summary>
        public ObservableCollection<CategoryGroup> Groups { get; }

        /// <summary>Minimale Protokoll-Tab-Grundlage: sammelt die bereits vorhandenen
        /// Status-Texte (Download/Copy/Ventoy) fortlaufend statt sie nur zu überschreiben — kein
        /// neuer Worker/Log-Kanal, siehe AppendLog().</summary>
        public ObservableCollection<string> LogEntries { get; }

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
            set { if (SetField(ref _selectedDrive, value)) { RaiseAllCommandsCanExecuteChanged(); OnPropertyChanged(nameof(DriveInfoText)); } }
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
            private set { if (SetField(ref _downloadStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
        }

        private string _copyStatus = string.Empty;
        public string CopyStatus
        {
            get => _copyStatus;
            private set { if (SetField(ref _copyStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
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
            // Kein automatisches AppendLog hier (anders als DownloadStatus/CopyStatus): VentoyStatus
            // enthält bereits den GESAMTEN akkumulierten Mehrzeilen-Text (siehe ConfirmVentoyAsync),
            // ein Log-Eintrag pro Setter-Aufruf würde also wachsend dieselben alten Zeilen erneut
            // protokollieren. Stattdessen loggen die Aufrufstellen in ConfirmVentoyAsync jede neue
            // Zeile einzeln direkt über AppendLog(...).
            private set { if (SetField(ref _ventoyStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); } }
        }

        public string LanguageButtonLabel => LocalizationService.Current == AppLanguage.German ? "EN" : "DE";
        public string SearchPlaceholder   => LocalizationService.T(Str.Linux_Toolbar_SearchPlaceholder);
        public string RefreshLabel        => LocalizationService.T(Str.Linux_Toolbar_Refresh);

        // ── Neue Beschriftungen für das Windows-angeglichene Layout (Phase A). Alle als
        // berechnete Properties statt fester XAML-Strings, damit ToggleLanguage() sie per
        // OnPropertyChanged(null) (= "alle Properties geändert", siehe ViewModelBase) live
        // umschalten kann — vermeidet die im Projekt-CLAUDE.md dokumentierte Falle
        // hartkodierter, nicht auf Sprachwechsel reagierender Button-Texte. ──
        public string HeaderSubtitle          => LocalizationService.T(Str.Linux_Header_Subtitle);
        public string SettingsLabel           => LocalizationService.T(Str.Linux_Header_Settings);
        public string HelpLabel               => LocalizationService.T(Str.Linux_Header_Help);
        public string NotAvailableTooltip     => LocalizationService.T(Str.Linux_Header_NotAvailableTooltip);
        public string TargetDriveLabel        => LocalizationService.T(Str.Linux_Toolbar_TargetDrive);
        public string SecureBootLabel         => LocalizationService.T(Str.Linux_Toolbar_SecureBoot);
        public string TabIsoSelectionLabel    => LocalizationService.T(Str.Linux_Tab_IsoSelection);
        public string TabLogLabel             => LocalizationService.T(Str.Linux_Tab_Log);
        public string TabStatusLabel          => LocalizationService.T(Str.Linux_Tab_Status);
        public string ColumnDistributionLabel => LocalizationService.T(Str.Linux_Column_Distribution);
        public string ColumnLocalLabel        => LocalizationService.T(Str.Linux_Column_Local);
        public string ColumnOnStickLabel      => LocalizationService.T(Str.Linux_Column_OnStick);
        public string ColumnCurrentLabel      => LocalizationService.T(Str.Linux_Column_Current);
        public string LogClearLabel           => LocalizationService.T(Str.Linux_Log_Clear);
        public string StatusCurrentOperationLabel => LocalizationService.T(Str.Linux_Status_CurrentOperation);
        public string StatusIdleLabel         => LocalizationService.T(Str.Linux_Status_Idle);
        public string ActionDownloadLabel     => LocalizationService.T(Str.Linux_Actions_Download);
        public string ActionCheckUpdatesLabel => LocalizationService.T(Str.Linux_Actions_CheckUpdates);
        public string ActionCheckUrlsLabel    => LocalizationService.T(Str.Linux_Actions_CheckUrls);
        public string ActionSearchIsoLabel    => LocalizationService.T(Str.Linux_Actions_SearchIso);
        public string ActionDatabaseLabel     => LocalizationService.T(Str.Linux_Actions_Database);
        public string ActionDbHealthCheckLabel => LocalizationService.T(Str.Linux_Actions_DbHealthCheck);
        public string ActionCopyToStickLabel  => LocalizationService.T(Str.Linux_Actions_CopyToStick);
        public string ActionVerifyIntegrityLabel => LocalizationService.T(Str.Linux_Actions_VerifyIntegrity);
        public string ActionGitHubTokenLabel  => LocalizationService.T(Str.Linux_Actions_GitHubToken);
        public string ActionNotAvailableTooltip => LocalizationService.T(Str.Linux_Actions_NotAvailableTooltip);
        public string VentoyInstallLabel      => LocalizationService.T(Str.Linux_Ventoy_Install);
        public string VentoyUpdateLabel       => LocalizationService.T(Str.Linux_Ventoy_Update);
        public string VentoyConfirmYesLabel   => LocalizationService.T(Str.Linux_Ventoy_ConfirmYes);
        public string VentoyConfirmCancelLabel => LocalizationService.T(Str.Linux_Ventoy_ConfirmCancel);
        public string ShowInfoOnHoverLabel    => LocalizationService.T(Str.Linux_StatusBar_ShowInfoOnHover);

        private bool _showInfoOnHover = true;
        /// <summary>Windows-Statusleisten-Checkbox "Info-Fenster (Mouseover)" — steuert, ob der
        /// Zeilen-Tooltip (Distro-Beschreibung) angezeigt wird.</summary>
        public bool ShowInfoOnHover
        {
            get => _showInfoOnHover;
            set => SetField(ref _showInfoOnHover, value);
        }

        /// <summary>Windows-Toolbar "Frei: x GB / y GB" — hier vereinfacht auf den freien Speicher
        /// des gewählten Laufwerks (kein Gesamt-/Ist-Vergleich, da DriveInfo unter Linux nur die
        /// Gesamtgröße aus lsblk kennt, nicht den tatsächlichen freien Platz ohne Mount).</summary>
        public string DriveInfoText
        {
            get
            {
                if (SelectedDrive?.MountPoint is null) return string.Empty;
                try
                {
                    var drive = new System.IO.DriveInfo(SelectedDrive.MountPoint);
                    if (!drive.IsReady) return string.Empty;
                    double freeGb = drive.AvailableFreeSpace / 1_000_000_000.0;
                    return string.Format(LocalizationService.T(Str.Linux_Toolbar_FreeSpaceFormat), $"{freeGb:F1} GB");
                }
                catch { return string.Empty; }
            }
        }

        /// <summary>Status-Tab: ob gerade irgendein Vorgang eine Statuszeile zu zeigen hat —
        /// steuert den Wechsel zwischen den drei Status-Zeilen und dem "Kein Vorgang aktiv."-Text.</summary>
        public bool HasAnyStatus =>
            !string.IsNullOrEmpty(DownloadStatus) || !string.IsNullOrEmpty(CopyStatus) || !string.IsNullOrEmpty(VentoyStatus);

        /// <summary>Fügt eine Zeile zum Protokoll-Tab hinzu. Kein Zeitstempel/Rotation wie beim
        /// Windows-`AppendLog` — bewusst minimal für Phase A, siehe Plan.</summary>
        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            LogEntries.Add(line);
        }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ToggleLanguageCommand { get; }
        public RelayCommand DownloadCommand { get; }
        public RelayCommand CopyToStickCommand { get; }
        public RelayCommand RequestVentoyInstallCommand { get; }
        public RelayCommand RequestVentoyUpdateCommand { get; }
        public RelayCommand ConfirmVentoyCommand { get; }
        public RelayCommand CancelVentoyCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        /// <summary>Statusleisten-Text (Windows-Pendant: `StatusText`/`StatusLbl`) — zeigt die
        /// zuletzt geänderte der drei Vorgangs-Statuszeilen, sonst leer.</summary>
        public string StatusBarText =>
            !string.IsNullOrEmpty(VentoyStatus) ? VentoyStatus.Split('\n')[^1]
            : !string.IsNullOrEmpty(CopyStatus) ? CopyStatus
            : DownloadStatus;

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

            RebuildGroups();
        }

        /// <summary>Gruppiert die (bereits gefilterten) Rows nach Kategorie für die Windows-
        /// angeglichene Listendarstellung — siehe Groups-Property. Läuft nach jedem ApplyFilter().</summary>
        private void RebuildGroups()
        {
            Groups.Clear();
            foreach (string key in Constants.Categories)
            {
                var entries = Rows.Where(r => r.CategoryKey == key).ToList();
                if (entries.Count > 0)
                    Groups.Add(new CategoryGroup(Constants.CategoryLabel(key), entries));
            }
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
            ApplyFilter();
            // Broadcastet ALLE Properties (leerer/null-Name = WPF/Avalonia-Konvention "alles
            // geändert") statt jedes neue Label einzeln aufzuzählen — deckt automatisch auch jedes
            // künftig ergänzte *Label ab, ohne ToggleLanguage() jedes Mal erweitern zu müssen.
            OnPropertyChanged(null);
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
                ? string.Format(LocalizationService.T(Str.Linux_Ventoy_ConfirmUpdate), SelectedDrive.DeviceNode, sizeGb)
                : string.Format(LocalizationService.T(Str.Linux_Ventoy_ConfirmInstall), SelectedDrive.DeviceNode, sizeGb);
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
                    onLog: line => { VentoyStatus = string.IsNullOrEmpty(VentoyStatus) ? line : VentoyStatus + "\n" + line; AppendLog(line); },
                    CancellationToken.None).ConfigureAwait(true);
                string result = ok ? LocalizationService.T(Str.Linux_Ventoy_ResultSuccess) : LocalizationService.T(Str.Linux_Ventoy_ResultFailed);
                VentoyStatus = string.IsNullOrEmpty(VentoyStatus) ? result : VentoyStatus + "\n" + result;
                AppendLog(result);
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
