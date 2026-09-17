using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Core.Workers;
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
        private readonly LinuxUsbService _usbService;
        private readonly VentoyInstallService _ventoyInstallService;

        public LinuxMainViewModel(
            IIsoDatabaseService db, string downloadDirectory,
            string? settingsIniPath = null,
            LinuxUsbService? usbService = null, VentoyInstallService? ventoyInstallService = null)
        {
            _db = db;
            _downloadDirectory = downloadDirectory;
            _settingsIniPath = settingsIniPath ?? LinuxPaths.SettingsIni;
            _usbService = usbService ?? new LinuxUsbService();
            _ventoyInstallService = ventoyInstallService ?? new VentoyInstallService();

            Categories = new ObservableCollection<CategoryOption>();
            Rows = new ObservableCollection<LinuxIsoRow>();
            Groups = new ObservableCollection<CategoryGroup>();
            LogEntries = new ObservableCollection<string>();
            Drives = new ObservableCollection<LinuxBlockDevice>();
            RebuildCategories();

            // BUGFIX (Nutzerfund: Katalog leert sich nach Klick auf "Aktualisieren" waehrend eines
            // laufenden Online-Scans): Refresh() ruft _db.Load() auf, das die von
            // TriggerAutoVersionCheckAsync/RunHealthCheckAsync gerade iterierte/gespeicherte
            // Entries-Liste mitten im Scan leert und neu befuellt -- ein torn read auf die INI-
            // Datei waehrend eines parallelen _db.Save() aus dem Scan kann dabei eine (fast) leere
            // Datei zurueckliefern. Jeder andere Scan-ausloesende Befehl hat bereits eine
            // !IsBusy/!HealthCheckActive-Absicherung; hier fehlte sie komplett.
            RefreshCommand        = new RelayCommand(Refresh, () => !IsBusy && !HealthCheckActive && !OnlineScanActive);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            // Windows-Pendant: kein gebundenes DownloadCommand — BtnDownload_Click im Code-behind
            // (MainWindow.axaml.cs) klärt die Dialogkette (Kopiermodus/Freispeicher/Slots) und ruft
            // DownloadQueueAsync(queue, ...) direkt auf, da die Dialoge ein Owner-Fenster brauchen.
            // Nutzerfund (2026-09-04): Klick auf "Abbrechen" während der Kopier-Phase (nach dem
            // Download) tat nichts — _activeDownloadWorker zeigt dort noch auf den längst FERTIGEN
            // DownloadWorker, die laufende Kopier-Pipeline (_pipelineCopyCts, siehe
            // DownloadQueueAsync) war nie erreichbar. Beide Phasen jetzt hier abgedeckt.
            CancelDownloadCommand  = new RelayCommand(
                () => { _activeDownloadWorker?.Cancel(); _pipelineCopyCts?.Cancel(); },
                () => IsBusy && (_activeDownloadWorker is not null || _pipelineCopyCts is not null));
            RequestFasterMirrorCommand = new RelayCommand<string>(name => { if (name is not null) _activeDownloadWorker?.RequestFasterMirror(name); });
            CopyToStickCommand     = new RelayCommand(() => _ = CopySelectedToStickAsync(), () => SelectedRow is not null && SelectedDrive is not null && !IsBusy);
            RequestVentoyInstallCommand = new RelayCommand(() => RequestVentoyConfirmation(updateMode: false), () => SelectedDrive is not null && !IsBusy);
            RequestVentoyUpdateCommand  = new RelayCommand(() => RequestVentoyConfirmation(updateMode: true),  () => SelectedDrive is not null && !IsBusy);
            ConfirmVentoyCommand   = new RelayCommand(() => _ = ConfirmVentoyAsync(), () => PendingConfirmationMessage is not null && !IsBusy);
            CancelVentoyCommand    = new RelayCommand(() => PendingConfirmationMessage = null, () => PendingConfirmationMessage is not null);
            ClearLogCommand        = new RelayCommand(() => LogEntries.Clear());
            RunHealthCheckCommand  = new RelayCommand(() => _ = RunHealthCheckAsync(), () => !IsBusy && !HealthCheckActive);
            VerifyIntegrityCommand = new RelayCommand(() => _ = VerifyStickIntegrityAsync(), () => SelectedDrive is not null && !IsBusy);
            CheckUpdatesCommand    = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !IsBusy);
            CheckUrlsCommand       = new RelayCommand(() => _ = CheckUrlsAsync(), () => !IsBusy);

            _gitHubToken = IniService.Read(_settingsIniPath, "App", "GitHubToken", string.Empty);
            HttpService.Instance.GitHubToken = _gitHubToken;

            // Windows-Pendant: MainViewModel.Initialize() ruft DeduplicateEntries() direkt nach
            // dem DB-Load auf, bevor der Baum/die Liste aufgebaut wird — Nutzerwunsch (2026-09-04):
            // "Duplikat-Schutz" prüfen/nachrüsten. _db ist zu diesem Zeitpunkt bereits geladen
            // (App.axaml.cs ruft IsoDatabaseService.Instance.Load() VOR diesem Konstruktor).
            DeduplicateEntries();
            NormalizeEntryNames();
            ApplyFilter();
        }

        /// <summary>Für die Windows-parallele Download-Dialogkette im Code-behind (Freispeicher-
        /// Check, "kein Stick"-Meldung) — Windows-Pendant: AppPaths.Instance.DownloadDir, dort
        /// direkt statisch erreichbar, hier über den ViewModel-Konstruktorparameter gekapselt.</summary>
        public string DownloadDirectory => _downloadDirectory;

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
            CancelDownloadCommand.RaiseCanExecuteChanged();
            CopyToStickCommand.RaiseCanExecuteChanged();
            RequestVentoyInstallCommand.RaiseCanExecuteChanged();
            RequestVentoyUpdateCommand.RaiseCanExecuteChanged();
            ConfirmVentoyCommand.RaiseCanExecuteChanged();
            RunHealthCheckCommand.RaiseCanExecuteChanged();
            VerifyIntegrityCommand.RaiseCanExecuteChanged();
            CheckUpdatesCommand.RaiseCanExecuteChanged();
            CheckUrlsCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
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

        private DownloadWorker? _activeDownloadWorker;
        /// <summary>Windows-Pendant: kein direktes Gegenstück nötig (Windows bricht die Pipeline-Kopie
        /// nicht separat abbrechbar ab) — hier gebraucht, weil die Kopier-Pipeline (siehe
        /// DownloadQueueAsync/RunPipelineCopyConsumerAsync) kein eigenes Worker-Objekt mit Cancel()
        /// hat, das der bestehende CancelDownloadCommand aufrufen könnte. Ohne dieses Feld wäre
        /// "Abbrechen" während der Kopier-Phase wirkungslos (_activeDownloadWorker ist dort schon
        /// fertig) — derselbe Bug, der schon einmal für den (inzwischen ersetzten) Batch-Kopier-Pfad
        /// gefunden und behoben wurde (Nutzerfund 2026-09-04, siehe CancelDownloadCommand-Kommentar).</summary>
        private CancellationTokenSource? _pipelineCopyCts;


        // ── Für das Windows-parallele DownloadProgressDialog im Code-behind (braucht ein Owner-
        // Fenster, siehe BtnDownload_Click) — Windows-Pendant: MainViewModel.DownloadItemProgress/
        // DownloadBatchCompleted/CopyItemProgress/CopyBatchCompleted (gleiche Namen/Signaturen). ──
        public event Action<string, int, string, bool>? DownloadItemProgress;
        public event Action<int, int>? DownloadBatchCompleted;
        public event Action<string, int, string>? CopyItemProgress;
        public event Action<int>? CopyBatchCompleted;

        private string _copyStatus = string.Empty;
        public string CopyStatus
        {
            get => _copyStatus;
            private set { if (SetField(ref _copyStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
        }

        private string _integrityStatus = string.Empty;
        public string IntegrityStatus
        {
            get => _integrityStatus;
            private set { if (SetField(ref _integrityStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
        }

        private string _updateCheckStatus = string.Empty;
        public string UpdateCheckStatus
        {
            get => _updateCheckStatus;
            private set { if (SetField(ref _updateCheckStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
        }

        private string _urlCheckStatus = string.Empty;
        public string UrlCheckStatus
        {
            get => _urlCheckStatus;
            private set { if (SetField(ref _urlCheckStatus, value)) { OnPropertyChanged(nameof(HasAnyStatus)); OnPropertyChanged(nameof(StatusBarText)); AppendLog(value); } }
        }

        // ── Automatischer Online-Versionscheck beim Start (Windows-Pendant: MainViewModel.
        // TriggerAutoVersionCheck/OnlineScanActive) — treibt den pulsierenden Hinweis oben mittig
        // in der Kopfzeile (ScanInProgress/ScanHintText), bewusst OHNE IsBusy zu setzen: Windows
        // sperrt während des Starts ebenfalls keine Buttons, zeigt nur eine Warnung. ──
        private bool _onlineScanActive;
        public bool OnlineScanActive
        {
            get => _onlineScanActive;
            private set { if (SetField(ref _onlineScanActive, value)) { OnPropertyChanged(nameof(ScanInProgress)); OnPropertyChanged(nameof(ScanHintText)); OnPropertyChanged(nameof(StartupHintText)); OnPropertyChanged(nameof(StartupHintPercent)); RefreshCommand.RaiseCanExecuteChanged(); } }
        }

        private int _onlineScanPercent;
        public int OnlineScanPercent
        {
            get => _onlineScanPercent;
            private set { if (SetField(ref _onlineScanPercent, value)) { OnPropertyChanged(nameof(ScanHintFullText)); OnPropertyChanged(nameof(StartupHintPercent)); } }
        }

        // ── Automatische USB-Stick-Erkennung (Windows-Pendant: MainViewModel.UsbScanActive) —
        // läuft, sobald PollDrivesAsync einen Ventoy-Stick neu auswählt (siehe ScanConnectedStickAsync).
        // Kein Prozentwert (Windows nutzt hier ebenfalls eine unbestimmte ProgressBar), daher kein
        // eigenes *Percent-Feld. ──
        private bool _usbScanActive;
        public bool UsbScanActive
        {
            get => _usbScanActive;
            private set { if (SetField(ref _usbScanActive, value)) { OnPropertyChanged(nameof(ScanInProgress)); OnPropertyChanged(nameof(ScanHintText)); OnPropertyChanged(nameof(ScanHintFullText)); } }
        }

        public bool ScanInProgress => OnlineScanActive || UsbScanActive;
        public string ScanHintText => OnlineScanActive ? LocalizationService.T(Str.Main_ScanHint_Online)
                                     : UsbScanActive     ? LocalizationService.T(Str.Main_ScanHint_Usb)
                                     : string.Empty;

        /// <summary>Fertig formatierter Kopfzeilen-Hinweis inkl. Prozentangabe NUR während des
        /// Online-Scans (der Stick-Scan hat keinen Fortschrittswert, daher dort kein "(NN%)"-Anhang,
        /// der sonst einen veralteten Online-Scan-Prozentwert fälschlich mit anzeigen würde).</summary>
        public string ScanHintFullText => OnlineScanActive ? $"{ScanHintText} ({OnlineScanPercent}%)" : ScanHintText;

        // ── URL-Check als zweite Phase des Start-Checks (Nutzerwunsch 2026-09-16: "URLs prüfen"
        // soll im selben Bitte-warten-Fenster mit eigenem Fortschritt durchlaufen, nicht nur der
        // Versionscheck). UrlCheckWorker liefert bereits ProgressPercent — CheckUrlsAsync() nutzte
        // das bisher nicht, da nur der manuelle "URLs prüfen"-Button diese Methode aufrief und dort
        // kein eigener Fortschrittsbalken existierte. ──
        private bool _urlCheckActive;
        public bool UrlCheckActive
        {
            get => _urlCheckActive;
            private set { if (SetField(ref _urlCheckActive, value)) { OnPropertyChanged(nameof(StartupHintText)); OnPropertyChanged(nameof(StartupHintPercent)); } }
        }

        private int _urlCheckPercent;
        public int UrlCheckPercent
        {
            get => _urlCheckPercent;
            private set { if (SetField(ref _urlCheckPercent, value)) OnPropertyChanged(nameof(StartupHintPercent)); }
        }

        /// <summary>Kombinierter Text/Fortschritt für StartupCheckDialog — deckt beide Phasen des
        /// Start-Checks ab (erst Versionscheck, dann URL-Check), ohne dass der Dialog selbst
        /// zwischen ihnen unterscheiden muss.</summary>
        public string StartupHintText => OnlineScanActive ? ScanHintText
                                        : UrlCheckActive   ? LocalizationService.T(Str.Log_CheckingUrls)
                                        : string.Empty;
        public int StartupHintPercent => OnlineScanActive ? OnlineScanPercent
                                        : UrlCheckActive   ? UrlCheckPercent
                                        : 100;

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
        public string ActionCancelDownloadLabel => LocalizationService.T(Str.Db_Btn_Cancel);
        public string FasterMirrorTooltip     => LocalizationService.T(Str.Linux_Download_FasterMirrorTooltip);
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
            !string.IsNullOrEmpty(DownloadStatus) || !string.IsNullOrEmpty(CopyStatus)
            || !string.IsNullOrEmpty(VentoyStatus) || !string.IsNullOrEmpty(IntegrityStatus)
            || !string.IsNullOrEmpty(UpdateCheckStatus) || !string.IsNullOrEmpty(UrlCheckStatus);

        private string _gitHubToken = string.Empty;
        /// <summary>Optionales GitHub Personal Access Token — hebt nur das API-Limit für
        /// GitHub-basierte Distro-Resolver von 60 auf 5000 Anfragen/Std an (siehe
        /// HttpService.GitHubToken). Persistiert wie unter Windows in der Settings-Ini.</summary>
        public string GitHubToken
        {
            get => _gitHubToken;
            set
            {
                if (!SetField(ref _gitHubToken, value)) return;
                IniService.Write(_settingsIniPath, "App", "GitHubToken", value);
                HttpService.Instance.GitHubToken = value;
            }
        }

        private bool _healthCheckActive;
        public bool HealthCheckActive
        {
            get => _healthCheckActive;
            private set { if (SetField(ref _healthCheckActive, value)) { RunHealthCheckCommand.RaiseCanExecuteChanged(); RefreshCommand.RaiseCanExecuteChanged(); } }
        }

        private int _healthCheckPercent;
        public int HealthCheckPercent
        {
            get => _healthCheckPercent;
            private set => SetField(ref _healthCheckPercent, value);
        }

        /// <summary>Wird nach Abschluss von RunHealthCheckAsync() mit den Pro-Distro-Ergebnissen
        /// gefeuert — die View öffnet darauf den DbHealthCheckDialog (gleiches Owner-Code-behind-
        /// Muster wie Windows' MainWindow.xaml.cs, keine reine MVVM-Navigation, siehe Plan).</summary>
        public event Action<IReadOnlyList<VersionCheckEntryResult>>? HealthCheckCompleted;

        /// <summary>Fügt eine Zeile zum Protokoll-Tab hinzu. Kein Zeitstempel/Rotation wie beim
        /// Windows-`AppendLog` — bewusst minimal für Phase A, siehe Plan.</summary>
        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            LogEntries.Add(line);
        }

        /// <summary>Windows-Pendant: MainViewModel.ApplyResolvedUpdatesAndOfferStickUpdate() — übernimmt
        /// die von UpdateScanWorker/AutoVersionCheckWorker in RemoteUrl/RemoteFilename/RemoteVersion
        /// aufgelösten neuen Versionen SOFORT in die persistierten Felder (Url/Filename), statt sie nur
        /// als Laufzeit-Badge ("🆕 v...") stehen zu lassen. Nutzerfund (2026-09-06): "geladen wird die
        /// alte" — Url/Filename blieben nach einem Online-Check auf dem alten Stand hängen, bis
        /// irgendwann ein Download lief; ein bloßer Badge-Wechsel sah für den Nutzer wie ein
        /// funktionierender Check aus, tatsächlich aktualisierte sich der Katalog nie. Bewusst OHNE das
        /// Windows-Pendant "sofort Stick-Update anbieten" — kein entsprechender Linux-UI-Flow für ein
        /// spontanes Stick-Update-Angebot vorhanden, reiner Katalog-Übernahme-Teil.</summary>
        internal void ApplyResolvedUpdates(List<int> updates, bool anyUrlDiscovered)
        {
            var offerForDownload = new List<IsoEntry>();
            foreach (int i in updates)
            {
                if (i < 0 || i >= _db.Entries.Count) continue;
                var e = _db.Entries[i]; if (string.IsNullOrEmpty(e.RemoteUrl)) continue;
                // Nutzerwunsch (2026-09-16): ist die BISHERIGE Version bereits lokal oder auf dem
                // zuletzt gescannten Stick vorhanden, die neu gefundene gleich zum Download anbieten
                // (siehe UpdatesAvailableForDownload unten) — MUSS vor dem Überschreiben von
                // e.Filename geprüft werden, sonst würde IsAvailableAnywhere() faelschlich gegen
                // die neue, garantiert noch nie heruntergeladene Datei pruefen.
                if (e.IsAvailableAnywhere(_downloadDirectory)) offerForDownload.Add(e);
                // BUGFIX (Nutzerfund: Katalog-Name bei CachyOS/EndeavourOS aktualisiert sich nie):
                // die alte Fassung suchte die aus dem DATEINAMEN extrahierte Version im NAMEN --
                // bei Distros, deren Katalog-Name die Version in einem anderen Format traegt als
                // der Dateiname (CachyOS: Name "2026.03" vs. Dateiname "260308", EndeavourOS:
                // "2026.03" vs. "2026.03.06"), kam dieser Teilstring im Namen nie vor -- IndexOf
                // schlug fehl, die Umbenennung wurde fuer immer stillschweigend uebersprungen
                // (Url/Filename wurden trotzdem korrekt aktualisiert, nur der Name blieb stehen).
                // Jetzt wird die Version aus dem NAMEN selbst extrahiert (garantiert im Namen
                // vorhanden) und genau die ersetzt.
                string nameVer = HttpService.ExtractVersion(e.Name);
                string newVer = string.IsNullOrEmpty(e.RemoteVersion) ? HttpService.ExtractVersion(e.RemoteFilename) : e.RemoteVersion;
                e.Url = e.RemoteUrl; e.Filename = e.RemoteFilename;
                e.UpdateAvailable = false;
                if (!string.IsNullOrEmpty(nameVer) && !string.IsNullOrEmpty(newVer) && nameVer != newVer)
                {
                    int pos = e.Name.IndexOf(nameVer, StringComparison.Ordinal);
                    if (pos >= 0) { string on = e.Name; e.Name = e.Name[..pos] + newVer + e.Name[(pos + nameVer.Length)..]; AppendLog(string.Format(LocalizationService.T(Str.Log_NameUpdated), on, e.Name)); }
                }
            }
            if (updates.Count > 0) { _db.Save(); AppendLog(string.Format(LocalizationService.T(Str.Log_DbNewVersionsSaved), updates.Count)); }
            else if (anyUrlDiscovered) { _db.Save(); AppendLog(LocalizationService.T(Str.Log_DbNewSourcesSaved)); }
            bool namesNormalized = NormalizeEntryNames() > 0;
            if (DeduplicateEntries() > 0 || namesNormalized) ApplyFilter();
            if (offerForDownload.Count > 0) UpdatesAvailableForDownload?.Invoke(offerForDownload);
        }

        /// <summary>Nutzerwunsch (2026-09-16): MainWindow.axaml.cs bietet bei mindestens einem
        /// bereits lokal/auf dem Stick vorhandenen Eintrag mit gefundenem Update sofort den
        /// Download an (Ja/Nein-Rückfrage, danach derselbe Ablauf wie der Download-Button).</summary>
        public event Action<List<IsoEntry>>? UpdatesAvailableForDownload;

        /// <summary>Selbstheilung für Katalog-Einträge, deren Anzeige-Name eine ANDERE Versions-
        /// Zeichenfolge trägt als der maßgebliche Dateiname — z.B. ein Tippfehler in der
        /// mitgelieferten Standard-Datenbank (Dr.Web LiveDisk zeigte "9.0.1", tatsächliche Datei
        /// war "900") oder ein Distro mit inkompatiblem Namens-/Dateinamen-Versionsformat
        /// (CachyOS "2026.03" vs. Dateiname "260308"). Nutzerfund (2026-09-15): Dr.Web blieb auf
        /// "9.0.1" hängen, obwohl die DefaultDatabase längst auf "9.0.0" korrigiert war — greift
        /// nur bei einer BRANDNEUEN Datenbank (LoadDefaults(), siehe TipEn-Präzedenzfall/
        /// BackfillMissingTipEn), nicht bei einer bereits vorhandenen Nutzer-ulm_isos.ini. Da
        /// Dr.Web schon "aktuell" ist (kein echtes Update gefunden), lief die Umbenennung in
        /// ApplyResolvedUpdates nie (die läuft nur für echte Update-Funde). Bewusst NICHT für
        /// ImportedFromStick-Einträge (deren Name-Konvention der Nutzer/Stick-Fund bestimmt, nicht
        /// der kuratierte Katalog).</summary>
        private int NormalizeEntryNames()
        {
            int changed = 0;
            foreach (var e in _db.Entries)
            {
                if (e.ImportedFromStick || string.IsNullOrWhiteSpace(e.Filename)) continue;
                string nameVer = HttpService.ExtractVersion(e.Name);
                string fileVer = HttpService.ExtractVersion(e.Filename);
                if (string.IsNullOrEmpty(nameVer) || string.IsNullOrEmpty(fileVer) || nameVer == fileVer) continue;
                int pos = e.Name.IndexOf(nameVer, StringComparison.Ordinal);
                if (pos < 0) continue;
                string on = e.Name;
                e.Name = e.Name[..pos] + fileVer + e.Name[(pos + nameVer.Length)..];
                AppendLog(string.Format(LocalizationService.T(Str.Log_NameUpdated), on, e.Name));
                changed++;
            }
            if (changed > 0) _db.Save();
            return changed;
        }

        /// <summary>Windows-Pendant: MainViewModel.DeduplicateEntries() — 1:1 dieselbe Logik über
        /// dieselben, schon plattformneutral verlinkten DistroMatcher-Funktionen (kein Neuschreiben).
        /// Läuft einmal beim Start (Konstruktor, vor der ersten ApplyFilter()) und vor jedem
        /// Gesundheitscheck (RunHealthCheckAsync) — "Gesundheitscheck &amp; Duplikat-Schutz",
        /// Nutzerwunsch 2026-09-04: erst bereinigen, dann prüfen, damit nicht doppelt geprüft wird.
        /// Entfernt zuerst EXAKTE Dateiname-Duplikate, danach "gleiche Distro, andere Version"-
        /// Duplikate (behält den nicht-vom-Stick-importierten, ggf. neuesten Dateinamen).</summary>
        private int DeduplicateEntries()
        {
            bool changed = false; int removed = 0;

            foreach (int i in DistroMatcher.FindExactDuplicateIndicesByFilename(_db.Entries))
            { AppendLog(string.Format(LocalizationService.T(Str.Log_ExactDuplicateRemoved), _db.Entries[i].Name, _db.Entries[i].Filename)); _db.Remove(i); changed = true; removed++; }

            var processed = new HashSet<IsoEntry>();
            var snapshot = _db.Entries.ToList();

            for (int i = 0; i < snapshot.Count; i++)
            {
                var a = snapshot[i]; if (processed.Contains(a)) continue;
                var duplicates = snapshot.Skip(i + 1)
                    .Where(b => !processed.Contains(b) &&
                                !string.Equals(a.Filename, b.Filename, StringComparison.OrdinalIgnoreCase) &&
                                DistroMatcher.AreSameDistro(a, b))
                    .ToList();
                if (duplicates.Count == 0) continue;

                var allEntries = new List<IsoEntry> { a }.Concat(duplicates).ToList();
                var keeper = allEntries.OrderBy(e => e.ImportedFromStick ? 1 : 0).First();
                var newerDup = allEntries
                    .Where(e => e != keeper && !string.IsNullOrWhiteSpace(e.Filename) &&
                                DistroMatcher.IsVersionNewer(
                                    HttpService.ExtractVersion(e.Filename),
                                    HttpService.ExtractVersion(string.IsNullOrWhiteSpace(keeper.Filename) ? keeper.Name : keeper.Filename)))
                    .OrderByDescending(e => HttpService.ExtractVersion(e.Filename))
                    .FirstOrDefault();
                if (newerDup != null)
                {
                    string newVer = HttpService.ExtractVersion(newerDup.Filename);
                    string oldName = keeper.Name;
                    keeper.Filename = newerDup.Filename;
                    // BUGFIX: siehe ApplyResolvedUpdates oben -- fuer die Textersetzung im Namen die
                    // dort tatsaechlich vorkommende Version verwenden (nicht die Filename-Version,
                    // die z.B. bei CachyOS/EndeavourOS in einem anderen Format vorliegt und im Namen
                    // nie gefunden wuerde).
                    string nameVer = HttpService.ExtractVersion(keeper.Name);
                    if (!string.IsNullOrEmpty(nameVer) && !string.IsNullOrEmpty(newVer) && nameVer != newVer)
                    { int pos = keeper.Name.IndexOf(nameVer, StringComparison.Ordinal); if (pos >= 0) keeper.Name = keeper.Name[..pos] + newVer + keeper.Name[(pos + nameVer.Length)..]; }
                    AppendLog(string.Format(LocalizationService.T(Str.Log_Merged), oldName, keeper.Name, keeper.Filename));
                }

                var dupsToRemove = allEntries.Where(e => e != keeper).ToList();
                for (int di = dupsToRemove.Count - 1; di >= 0; di--)
                {
                    var dup = dupsToRemove[di];
                    int idx = _db.Entries.ToList().IndexOf(dup);
                    if (idx >= 0) { AppendLog(string.Format(LocalizationService.T(Str.Log_DuplicateRemoved), dup.Name)); _db.Remove(idx); changed = true; removed++; }
                    processed.Add(dup);
                }
                processed.Add(keeper);
            }
            if (changed) _db.Save();
            return removed;
        }

        /// <summary>Windows-Pendant: MainViewModel.AddImportedEntry() — der eigentliche "Duplikat-
        /// Schutz" beim Import (Online-Suche/Stick-Fund): erkennt ULM per DistroMatcher.AreSameDistro,
        /// dass eine "neue" ISO eigentlich einem bereits vorhandenen Katalog-Eintrag entspricht, wird
        /// KEIN doppelter Eintrag angelegt — stattdessen übernimmt der bestehende Eintrag den neuen
        /// Dateinamen (nur wenn er nachweislich neuer ist oder noch keiner hinterlegt war, siehe
        /// DistroMatcher.ShouldAdoptImportedFilename — sonst würde eine ältere, zufällig gefundene
        /// Datei den Katalog rückwärts degradieren).</summary>
        public void AddImportedEntry(IsoEntry e)
        {
            var existing = _db.Entries.FirstOrDefault(d => DistroMatcher.AreSameDistro(d, e));
            if (existing != null)
            {
                if (DistroMatcher.ShouldAdoptImportedFilename(existing.Filename, e.Filename))
                {
                    AppendLog(string.Format(LocalizationService.T(Str.Log_FilenameAdopted), e.Filename, existing.Name));
                    existing.Filename = e.Filename;
                    existing.ImportedFromStick = true;
                    _db.Save();
                }
                else
                    AppendLog(string.Format(LocalizationService.T(Str.Log_FilenameNotAdopted), e.Filename, existing.Name));
                return;
            }
            _db.Add(e);
            AppendLog(string.Format(LocalizationService.T(Str.Log_EntryAdded), e.Category, e.Name, e.Filename));
        }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand ToggleLanguageCommand { get; }
        public RelayCommand CancelDownloadCommand { get; }
        public RelayCommand<string> RequestFasterMirrorCommand { get; }
        public RelayCommand CopyToStickCommand { get; }
        public RelayCommand RequestVentoyInstallCommand { get; }
        public RelayCommand RequestVentoyUpdateCommand { get; }
        public RelayCommand ConfirmVentoyCommand { get; }
        public RelayCommand CancelVentoyCommand { get; }
        public RelayCommand ClearLogCommand { get; }
        public RelayCommand RunHealthCheckCommand { get; }
        public RelayCommand VerifyIntegrityCommand { get; }
        public RelayCommand CheckUpdatesCommand { get; }
        public RelayCommand CheckUrlsCommand { get; }

        /// <summary>Statusleisten-Text (Windows-Pendant: `StatusText`/`StatusLbl`) — zeigt die
        /// zuletzt geänderte der drei Vorgangs-Statuszeilen, sonst leer.</summary>
        public string StatusBarText =>
            !string.IsNullOrEmpty(VentoyStatus) ? VentoyStatus.Split('\n')[^1]
            : !string.IsNullOrEmpty(IntegrityStatus) ? IntegrityStatus
            : !string.IsNullOrEmpty(UrlCheckStatus) ? UrlCheckStatus
            : !string.IsNullOrEmpty(UpdateCheckStatus) ? UpdateCheckStatus
            : !string.IsNullOrEmpty(CopyStatus) ? CopyStatus
            : DownloadStatus;

        public void Refresh()
        {
            _db.Load();
            ApplyFilter();
        }

        /// <summary>Windows-Pendant: MainViewModel.RefreshAllEntries() — baut die Zeilenliste aus
        /// dem AKTUELLEN In-Memory-Stand neu auf, OHNE die Datenbank von der Festplatte neu zu
        /// laden (anders als Refresh()/RefreshCommand oben). Nutzerfund (2026-09-06): der
        /// Datenmüll-Schutz-Dialog (RunLocalFileMaintenanceAsync) rief bisher nach einer Löschung
        /// Refresh() auf — das ÜBERSCHREIBT die kompletten _db.Entries mit frisch von der Platte
        /// geparsten IsoEntry-Objekten und wischt dabei die Laufzeit-Felder RemoteVersion/
        /// RemoteUrl/RemoteFilename/UpdateAvailable weg (diese werden nie persistiert, siehe
        /// IsoDatabaseService-Kommentar) — der gerade erst durchgelaufene Online-Check-Stand
        /// ("Aktualität") ging dadurch verloren. Windows nutzt für exakt diesen Fall
        /// RefreshAllEntries() (kein DB-Reload, nur UI-Neuaufbau) — dieselbe Methode hier
        /// portiert.</summary>
        public void RefreshRows() => ApplyFilter();

        /// <summary>Zwischengespeicherte letzte Stick-Dateiliste (Nutzerfund 2026-09-04:
        /// "Auf dem Stick"-Spalte zeigte "Nein" für eine Distro, die nachweislich bereits
        /// vollständig auf dem Stick lag). Root Cause: ApplyStickResults lief bisher nur bei einem
        /// echten Scan-Ereignis (neu erkannter Stick / direkt nach einer Kopie) — wurde
        /// zwischenzeitlich z.B. erst durch einen Download IsoEntry.Filename gesetzt/geändert
        /// (URL-Auflösung setzt den Dateinamen oft erst zur Laufzeit), blieb die Klassifizierung
        /// bis zum nächsten echten Scan (der auf demselben Stick evtl. nie wieder auslöst, siehe
        /// _lastScannedDeviceNode-Schutz in PollDrivesAsync) auf dem alten Stand hängen — obwohl
        /// die Datei objektiv längst da war. Fix: die zuletzt gelesene Dateiliste bleibt hier
        /// gecacht; ApplyFilter() wendet sie bei jedem Aufruf erneut an (rein lokal, kein I/O/
        /// Netzwerk) — jede Änderung an Entry.Filename wirkt sich damit sofort aus, ohne auf den
        /// nächsten echten (I/O-lastigen) Stick-Scan warten zu müssen. Null = in dieser Sitzung
        /// noch nie erfolgreich gescannt (dann NICHT reklassifizieren, sonst würde vor dem ersten
        /// Scan fälschlich alles auf "Missing" gesetzt).</summary>
        private IReadOnlyList<UsbService.StickIso>? _lastStickListing;

        /// <summary>Für MainWindow.axaml.cs' StaleDuplicatesOnStickDetected-Handler — findet den
        /// physischen Pfad einer als veraltetes Duplikat gemeldeten Stick-Datei.</summary>
        public IReadOnlyList<UsbService.StickIso> LastStickListing => _lastStickListing ?? Array.Empty<UsbService.StickIso>();

        private void ApplyFilter()
        {
            if (SelectedDrive?.MountPoint is not null && _lastStickListing is not null)
                ApplyStickResults(_lastStickListing);

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

        /// <summary>Windows-Pendant: MainViewModel.GetSelectedEntries() — die per Zeilen-Checkbox
        /// angehakten Einträge, unabhängig von einer zusätzlichen SelectedRow-Markierung.</summary>
        public List<IsoEntry> GetSelectedEntries() => _db.Entries.Where(e => e.IsSelected).ToList();

        /// <summary>Windows-Pendant: MainViewModel.StartDownload() + DownloadWorker (Core/Workers/
        /// Workers.cs, bereits plattformneutral in ULM.Linux.csproj verlinkt, kein Neuschreiben).
        /// Die Warteschlangen-/Kopiermodus-/Slot-Klärung passiert wie unter Windows VOR diesem
        /// Aufruf im Code-behind (MainWindow.axaml.cs BtnDownload_Click, braucht ein Owner-Fenster
        /// für die Dialogkette) — diese Methode führt nur noch den bereits geklärten Auftrag aus,
        /// exakt wie MainViewModel.StartDownload(queue, drive, copyAfter, deleteAfter, slots).
        /// Nutzerfund (2026-09-06): "Kopie startet nicht sofort nach abgeschlossenem Download" —
        /// die vorherige Fassung kopierte SEQUENZIELL (erst alle Downloads fertig, dann Batch-
        /// Kopie). Jetzt wie Windows (StartDownload usePipeline/RunPipelineCopyConsumerAsync) eine
        /// Channel-basierte Pipeline: jede fertig heruntergeladene Distro wird SOFORT auf den
        /// Stick kopiert, während andere Downloads noch parallel laufen können.</summary>
        public async Task DownloadQueueAsync(List<IsoEntry> queue, string? mountPoint, bool copyAfter, bool deleteAfter, int slots)
        {
            if (IsBusy || queue.Count == 0) return;

            IsBusy = true;
            DownloadPercent = 0;
            DownloadStatus = string.Empty;
            AppendLog(string.Format(LocalizationService.T(Str.Log_DownloadStarted), queue.Count, slots)
                + (mountPoint is null ? "" : string.Format(LocalizationService.T(Str.Log_ToDriveSuffix), mountPoint)));
            foreach (var e in queue) AppendLog(string.Format(LocalizationService.T(Str.Log_QueueItem), e.Name));

            var worker = new DownloadWorker(queue, slots, _downloadDirectory, _db, mountPoint ?? string.Empty, copyAfter, deleteAfter);
            _activeDownloadWorker = worker;
            CancelDownloadCommand.RaiseCanExecuteChanged();

            worker.LogMessage += msg => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendLog(msg));
            // Nutzerfund (2026-09-05): sowohl die ursprüngliche Fassung (verschachteltes
            // Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult(), UI-Thread-Selbst-
            // Deadlock) ALS AUCH die erste Korrektur (ein Hintergrund-Thread blockiert per
            // TaskCompletionSource auf eine über Dispatcher.UIThread.Post gezeigte Rückfrage) haben
            // die App beim ersten dauerhaft langsamen Mirror komplett einfrieren lassen — ohne
            // Debugger-Zugriff (kein ptrace hier) ließ sich die zweite Fassung nicht mehr sicher
            // isoliert nachweisen. Statt ein drittes, ebenso fragiles Cross-Thread-Blockier-Schema
            // zu versuchen: Rückfrage komplett entfernt, automatisch weiterlaufen lassen (nur Log-
            // Hinweis, keine Blockierung, kein Dispatcher-Aufruf) — dieser Pfad betrifft ohnehin nur
            // einen seltenen Randfall (dauerhaft <1MB/s trotz ausgeschöpfter Mirror-Alternativen),
            // ein garantiert nicht einfrierender automatischer Fortsetzungs-Entscheid ist hier
            // eindeutig wichtiger als die Interaktivität dieser einen Frage.
            worker.ConfirmSlowDownloadAnyway = (name, host) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    AppendLog(string.Format(LocalizationService.T(Str.Msg_SlowDownload_Body), name, host)));
                return true;
            };

            // Windows-Pendant: StartDownload usePipeline. Sobald kopiert werden soll UND ein Stick
            // ausgewählt ist, läuft ein paralleler Consumer, der jede erfolgreich heruntergeladene
            // Distro sofort über den Channel entgegennimmt und kopiert (RunPipelineCopyConsumerAsync
            // unten) — statt wie vorher bis zum Ende des GESAMTEN Download-Stapels zu warten.
            bool usePipeline = copyAfter && mountPoint is not null;
            System.Threading.Channels.Channel<IsoEntry>? pipelineChannel = null;
            Task<(int Ok, int Failed)>? pipelineTask = null;
            if (usePipeline)
            {
                pipelineChannel = System.Threading.Channels.Channel.CreateUnbounded<IsoEntry>(new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
                var channelReader = pipelineChannel.Reader; string capMount = mountPoint!;
                _pipelineCopyCts = new CancellationTokenSource();
                CancelDownloadCommand.RaiseCanExecuteChanged();
                pipelineTask = RunPipelineCopyConsumerAsync(channelReader, capMount, deleteAfter, _pipelineCopyCts.Token);
                worker.ItemCompleted += (entry, success) =>
                {
                    if (success && entry.IsLocallyAvailable(_downloadDirectory))
                    {
                        pipelineChannel.Writer.TryWrite(entry);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            UpdateRowLiveStatus(entry.Name, LocalizationService.T(Str.CpStatus_Starting), false);
                            DownloadItemProgress?.Invoke(entry.Name, 100, LocalizationService.T(Str.CpStatus_Starting), false);
                            AppendLog(string.Format(LocalizationService.T(Str.Log_MovedToCopyQueue), entry.Name));
                        });
                    }
                };
            }
            else
            {
                worker.ItemCompleted += (entry, success) =>
                {
                    if (success) Avalonia.Threading.Dispatcher.UIThread.Post(() => entry.IsSelected = false);
                };
            }

            worker.OverallProgress += (pct, detail) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { DownloadPercent = pct; DownloadStatus = detail; });
            worker.SlotUpdated += p => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateRowLiveStatus(p.IsoName, $"{p.Percent}% {p.Status}", p.CanRequestFasterMirror);
                DownloadItemProgress?.Invoke(p.IsoName, p.Percent, p.Status, p.CanRequestFasterMirror);
            });

            // Nutzerfund (2026-09-06): nach einem Download+Kopier-Durchlauf blieb IsBusy hängen —
            // weder der automatische USB-Scan (PollDrivesAsync prüft !IsBusy) noch "Auf Stick
            // kopieren" (CopyToStickCommand prüft ebenfalls !IsBusy) reagierten danach noch.
            // BUGFIX: ohne try/finally hätte JEDE unerwartete Exception irgendwo in diesem Block
            // (Download-Worker, Kopier-Pipeline, Ventoy-Menü-Update, Stick-Scan) IsBusy für den
            // Rest der Sitzung dauerhaft auf true stehen lassen — kein einzelner bestätigter
            // Reproduktionsschritt dafür gefunden, aber die einzige Stelle im gesamten Ablauf ohne
            // jede Absicherung. Jetzt garantiert IsBusy=false, egal was dazwischen schiefgeht.
            int okCount = 0, failedCount = 0, copyOk = 0, copyFailed = 0;
            try
            {
                var tcs = new TaskCompletionSource<(int Ok, int Failed)>();
                worker.Completed += (ok, failed, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => tcs.TrySetResult((ok, failed)));
                await worker.RunAsync().ConfigureAwait(true);
                (okCount, failedCount) = await tcs.Task.ConfigureAwait(true);
                _db.Save();
                // Download-Phase vorbei — ab hier zeigt _activeDownloadWorker sonst fälschlich auf
                // einen längst fertigen Worker, während die Kopier-Phase (falls sie folgt) über
                // _pipelineCopyCts läuft. Siehe CancelDownloadCommand-Kommentar oben.
                _activeDownloadWorker = null;
                DownloadBatchCompleted?.Invoke(okCount, failedCount);

                if (usePipeline)
                {
                    pipelineChannel!.Writer.Complete();
                    (copyOk, copyFailed) = await pipelineTask!.ConfigureAwait(true);
                    UsbService.UpdateVentoyMenu(mountPoint!, _db.Entries);
                    _db.Save();
                    CopyBatchCompleted?.Invoke(copyOk);
                    // Nutzerfund (2026-09-04): "Auf dem Stick"-Spalte blieb nach dem Kopieren auf dem
                    // alten Stand — ScanConnectedStickAsync (setzt UsbStatus) lief bisher NUR über
                    // PollDrivesAsync' "neu erkannter Stick"-Zweig, nie nach einem Kopiervorgang auf
                    // einen bereits bekannten Stick. Direkter Aufruf hier (nicht über PollDrivesAsync'
                    // _lastScannedDeviceNode-Schutz, der genau diesen Fall absichtlich überspringt)
                    // schließt die Lücke, ohne den Neu-Erkennungs-Pfad zu verändern.
                    await ScanConnectedStickAsync(mountPoint!).ConfigureAwait(true);
                }
            }
            finally
            {
                _pipelineCopyCts?.Dispose();
                _pipelineCopyCts = null;
                _activeDownloadWorker = null;
                IsBusy = false;
                ApplyFilter();
                DownloadStatus = BuildDownloadSummary(okCount, failedCount, copyAfter, copyOk, copyFailed, mountPoint);
                CancelDownloadCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Windows-Pendant: MainViewModel.RunPipelineCopyConsumerAsync() — kopiert jede
        /// über den Channel hereinkommende Distro einzeln auf den Stick, sobald ihr Download fertig
        /// ist, während andere Downloads noch laufen können (siehe DownloadQueueAsync oben).
        /// Bewusst eine eigenständige Kopierschleife statt CopyToUsbWorker-Wiederverwendung (der
        /// nimmt eine feste Liste am Anfang entgegen, keine laufend nachgereichten Einträge) — an
        /// Puffergröße/Zielpfad-Konvention/Statustexten (Str.CpStatus_*, TransferFormat) trotzdem
        /// 1:1 an CopyToUsbWorker (Core/Workers/Workers.cs) angelehnt, damit beide Linux-Kopierwege
        /// gleich klingen, statt Windows' eigene (leicht abweichende) Log-Formulierungen zu
        /// übernehmen. Bewusst ABWEICHEND von Windows: löscht die Quelldatei nur, wenn deleteAfter
        /// tatsächlich gesetzt ist — Windows' RunPipelineCopyConsumerAsync löscht dort unbedingt,
        /// unabhängig vom "danach löschen"-Häkchen (sichtbar inkonsistent zum eigenen Batch-Pfad
        /// StartCopyToStick, der deleteAfter korrekt prüft); hier bewusst korrekt gehalten statt
        /// diese Windows-Inkonsistenz nachzubauen.</summary>
        internal async Task<(int Ok, int Failed)> RunPipelineCopyConsumerAsync(
            System.Threading.Channels.ChannelReader<IsoEntry> reader, string mountPoint, bool deleteAfter, CancellationToken token)
        {
            const int bufSize = 4 * 1024 * 1024;
            byte[] buf = new byte[bufSize];
            int copyOkCount = 0, copyFailedCount = 0;
            string root = UsbService.DriveRoot(mountPoint);

            try
            {
                await foreach (var entry in reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    string srcPath = System.IO.Path.Combine(_downloadDirectory, entry.Filename);
                    string entryName = entry.Name;
                    if (!System.IO.File.Exists(srcPath))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            CopyItemProgress?.Invoke(entryName, 0, LocalizationService.T(Str.CpStatus_Cancelled));
                            AppendLog(string.Format(LocalizationService.T(Str.Log_SourceFileNotFound), entryName));
                        });
                        copyFailedCount++; continue;
                    }
                    long fileSize;
                    string targetDir, targetPath;
                    try
                    {
                        // BUGFIX (Review): FileInfo.Length und Directory.CreateDirectory standen
                        // hier bisher UNGESCHÜTZT — eine Race Condition (Quelldatei verschwindet
                        // zwischen dem File.Exists oben und hier, z.B. durch eine parallel
                        // laufende Datenmüll-Bereinigung) oder ein Zielverzeichnis-Fehler hätte
                        // sonst eine unbehandelte Exception aus DIESER Methode heraus bis zu
                        // DownloadQueueAsync durchgereicht — IsBusy wäre dann NIE zurückgesetzt
                        // worden (siehe try/finally-Kommentar dort), der komplette Rest der
                        // Sitzung (USB-Scan, "Auf Stick kopieren") wäre danach wirkungslos
                        // geblieben. Jetzt bleibt ein Fehlschlag auf DIESEN einen Eintrag begrenzt.
                        fileSize = new System.IO.FileInfo(srcPath).Length;
                        targetDir = System.IO.Path.Combine(root, entry.Category);
                        System.IO.Directory.CreateDirectory(targetDir);
                        targetPath = System.IO.Path.Combine(targetDir, entry.Filename);
                    }
                    catch (Exception ex)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            CopyItemProgress?.Invoke(entryName, 0, string.Format(LocalizationService.T(Str.DlStatus_GeneralError), ex.Message));
                            AppendLog(string.Format(LocalizationService.T(Str.Log_CopyError), entryName, ex.Message));
                        });
                        copyFailedCount++; continue;
                    }
                    try
                    {
                        var drv = new System.IO.DriveInfo(root);
                        if (drv.IsReady && drv.AvailableFreeSpace < fileSize)
                        {
                            double neededGb = fileSize / 1_073_741_824.0, freeGb = drv.AvailableFreeSpace / 1_073_741_824.0;
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                CopyItemProgress?.Invoke(entryName, 0, LocalizationService.T(Str.CpStatus_Cancelled));
                                AppendLog(string.Format(LocalizationService.T(Str.Log_NotEnoughSpace), entryName, mountPoint, neededGb.ToString("F2"), freeGb.ToString("F2")));
                            });
                            copyFailedCount++; continue;
                        }
                    }
                    catch { /* Freispeicher-Check ist best-effort, analog zu CopyToUsbWorker */ }

                    long copied = 0L; var sw = System.Diagnostics.Stopwatch.StartNew(); long lastMark = 0L; double lastEl = 0.0;
                    bool copyOk;
                    try
                    {
                        using var src = new System.IO.FileStream(srcPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, bufSize, System.IO.FileOptions.SequentialScan | System.IO.FileOptions.Asynchronous);
                        using var dest = new System.IO.FileStream(targetPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, bufSize, System.IO.FileOptions.Asynchronous);
                        int read;
                        while ((read = await src.ReadAsync(buf, token).ConfigureAwait(false)) > 0)
                        {
                            await dest.WriteAsync(buf.AsMemory(0, read), token).ConfigureAwait(false);
                            copied += read;
                            double el = sw.Elapsed.TotalSeconds;
                            if (el - lastEl >= 0.4 || copied >= fileSize)
                            {
                                double bps = (copied - lastMark) / Math.Max(0.001, el - lastEl);
                                lastMark = copied; lastEl = el;
                                int pct = fileSize > 0 ? (int)((copied * 100L) / fileSize) : 0;
                                string det = TransferFormat.BuildDetail(bps, copied, fileSize);
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    UpdateRowLiveStatus(entryName, $"{pct}% {det}", false);
                                    CopyItemProgress?.Invoke(entryName, pct, det);
                                });
                            }
                        }
                        await dest.FlushAsync(token).ConfigureAwait(false);
                        copyOk = new System.IO.FileInfo(targetPath).Length == fileSize;
                        if (!copyOk) { try { System.IO.File.Delete(targetPath); } catch { } }
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.Delete(targetPath); } catch { }
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            CopyItemProgress?.Invoke(entryName, 0, string.Format(LocalizationService.T(Str.DlStatus_GeneralError), ex.Message));
                            AppendLog(string.Format(LocalizationService.T(Str.Log_CopyError), entryName, ex.Message));
                        });
                        copyFailedCount++; continue;
                    }
                    if (!copyOk)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => CopyItemProgress?.Invoke(entryName, 0, LocalizationService.T(Str.CpStatus_Cancelled)));
                        copyFailedCount++; continue;
                    }
                    copyOkCount++;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (deleteAfter) IsoEntry.TryDelete(srcPath, line => AppendLog(line));
                        entry.IsSelected = false;
                        UpdateRowLiveStatus(entryName, LocalizationService.T(Str.CpStatus_Done), false);
                        CopyItemProgress?.Invoke(entryName, 100, LocalizationService.T(Str.CpStatus_Done));
                    });
                }
            }
            catch (OperationCanceledException) { /* Abbruch über CancelDownloadCommand — kein Fehler */ }
            return (copyOkCount, copyFailedCount);
        }

        private void UpdateRowLiveStatus(string isoName, string status, bool canRequestFasterMirror) =>
            Rows.FirstOrDefault(r => r.Entry.Name == isoName)?.SetLiveStatus(status, canRequestFasterMirror);

        private static string BuildDownloadSummary(int ok, int failed, bool copyAfter, int copyOk, int copyFailed, string? mountPoint)
        {
            if (ok == 0 && !copyAfter) return LocalizationService.T(Str.Log_NoDownloadsStatus);
            if (copyAfter && mountPoint is not null)
                return copyOk > 0
                    ? string.Format(LocalizationService.T(Str.Log_DownloadedAndCopiedStatus), copyOk, mountPoint)
                    : string.Format(LocalizationService.T(Str.Log_SomeFailedStatus), copyFailed + failed);
            string summary = string.Format(LocalizationService.T(Str.Log_DownloadedCountStatus), ok, ok + failed);
            return failed > 0 ? summary + string.Format(LocalizationService.T(Str.Log_FailedSuffix), failed) : summary;
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

        /// <summary>Windows-Pendant: MainViewModel.RunHealthCheck() (ViewModels/MainViewModel.cs
        /// ~Zeile 1349) — nutzt denselben plattformneutralen UpdateScanWorker aus
        /// Core/Workers/Workers.cs, nur das UI-Thread-Marshalling ist Avalonia- statt WPF-Dispatcher.</summary>
        public async Task RunHealthCheckAsync()
        {
            if (IsBusy || HealthCheckActive) return;
            // Windows-Pendant: RunHealthCheck() räumt zuerst Duplikate weg, damit nicht doppelt
            // geprüft wird ("Gesundheitscheck & Duplikat-Schutz", Nutzerwunsch 2026-09-04).
            bool namesNormalized = NormalizeEntryNames() > 0;
            if (DeduplicateEntries() > 0 || namesNormalized) ApplyFilter();
            HealthCheckActive = true;
            HealthCheckPercent = 0;
            var results = new List<VersionCheckEntryResult>();
            var worker = new UpdateScanWorker(_db.Entries, _downloadDirectory, checkAllEntries: true);
            worker.Progress += (c, t) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                HealthCheckPercent = t > 0 ? (c * 100) / t : 0);
            worker.EntryChecked += result => Avalonia.Threading.Dispatcher.UIThread.Post(() => results.Add(result));

            var tcs = new TaskCompletionSource();
            worker.Completed += (resolved, updates) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                HealthCheckActive = false;
                HealthCheckPercent = 100;
                // Windows-Pendant: HealthCheckCompleted (öffnet den modalen DbHealthCheckDialog) muss
                // VOR ApplyResolvedUpdates feuern, siehe dortiger Kommentar zur Dialog-Reihenfolge.
                HealthCheckCompleted?.Invoke(results);
                ApplyResolvedUpdates(updates, worker.AnyUrlDiscovered || worker.AnyStreakChanged);
                ApplyFilter();
                tcs.TrySetResult();
            });
            await worker.RunAsync().ConfigureAwait(true);
            await tcs.Task.ConfigureAwait(true);
        }

        /// <summary>Automatischer Online-Versionscheck beim Programmstart. Windows-Pendant:
        /// MainViewModel.TriggerAutoVersionCheck(). Läuft unaufgefordert (kein Button), treibt
        /// nur OnlineScanActive/-Percent (Kopfzeilen-Hinweis) — bewusst kein IsBusy, wie unter
        /// Windows. Live-Zeilen pro Distro landen im Protokoll; ein einzelner ApplyFilter()-Aufruf
        /// erst am Ende (statt pro Eintrag) vermeidet Listen-Geflacker/Auswahlverlust während des
        /// Laufs.</summary>
        /// <summary>Windows-Pendant: MainViewModel.AutoVersionCheckCompleted — feuert der
        /// Code-behind (MainWindow.axaml.cs) hier stößt danach einmalig RunLocalFileMaintenanceAsync()
        /// an ("Datenmüll-Schutz", Nutzerwunsch 2026-09-04).</summary>
        public event Action? AutoVersionCheckCompleted;

        public async Task TriggerAutoVersionCheckAsync()
        {
            if (_db.Entries.Count == 0) { AutoVersionCheckCompleted?.Invoke(); return; }
            OnlineScanActive = true; OnlineScanPercent = 0;
            var worker = new AutoVersionCheckWorker(_db.Entries, _downloadDirectory);
            worker.Progress += (c, t) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                OnlineScanPercent = t > 0 ? (c * 100) / t : 0);
            worker.EntryChecked += result => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                AppendLog(!result.Resolved
                    ? string.Format(LocalizationService.T(Str.Log_EntryUnreachable), result.Name)
                    : result.HasUpdate
                        ? string.Format(LocalizationService.T(Str.Log_UpdateFound), result.Name, result.LocalVersion, result.RemoteVersion)
                        : string.Format(LocalizationService.T(Str.Log_VersionCurrent), result.Name, result.RemoteVersion)));
            var tcs = new TaskCompletionSource();
            worker.Completed += (resolved, updates) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyResolvedUpdates(updates, worker.AnyUrlDiscovered || worker.AnyStreakChanged);
                OnlineScanActive = false; OnlineScanPercent = 100;
                ApplyFilter();
                AutoVersionCheckCompleted?.Invoke();
                tcs.TrySetResult();
            });
            await worker.RunAsync().ConfigureAwait(true);
            await tcs.Task.ConfigureAwait(true);
        }

        /// <summary>"Nach Updates suchen"-Button. Windows-Pendant: MainViewModel.OnCheckUpdates().</summary>
        public async Task CheckUpdatesAsync()
        {
            if (IsBusy) return;
            IsBusy = true; UpdateCheckStatus = LocalizationService.T(Str.Log_CheckingForUpdates);
            var worker = new UpdateScanWorker(_db.Entries, _downloadDirectory);
            worker.EntryChecked += result => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                AppendLog(!result.Resolved
                    ? string.Format(LocalizationService.T(Str.Log_ManualCheckUnreachable), result.Name)
                    : result.HasUpdate
                        ? string.Format(LocalizationService.T(Str.Log_UpdateFound), result.Name, result.LocalVersion, result.RemoteVersion)
                        : string.Format(LocalizationService.T(Str.Log_ManualCheckCurrent), result.Name, result.RemoteVersion)));
            var tcs = new TaskCompletionSource();
            worker.Completed += (resolved, updates) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (updates.Count > 0 || worker.AnyUrlDiscovered || worker.AnyStreakChanged) _db.Save();
                IsBusy = false;
                UpdateCheckStatus = updates.Count > 0 ? string.Format(LocalizationService.T(Str.Log_UpdatesFoundStatus), updates.Count)
                                  : resolved > 0       ? LocalizationService.T(Str.Log_AllCurrentSimpleStatus) : LocalizationService.T(Str.Log_NoLocalIsosStatus);
                ApplyFilter();
                tcs.TrySetResult();
            });
            await worker.RunAsync().ConfigureAwait(true);
            await tcs.Task.ConfigureAwait(true);
        }

        /// <summary>"URLs prüfen"-Button. Windows-Pendant: MainViewModel.OnCheckUrls().</summary>
        public async Task CheckUrlsAsync()
        {
            if (IsBusy) return;
            IsBusy = true; UrlCheckStatus = LocalizationService.T(Str.Log_CheckingUrls);
            UrlCheckActive = true; UrlCheckPercent = 0;
            var entries = _db.Entries.ToList();
            var worker = new UrlCheckWorker(entries);
            worker.ProgressPercent += p => Avalonia.Threading.Dispatcher.UIThread.Post(() => UrlCheckPercent = p);
            worker.EntryChecked += (i, ok) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (i >= 0 && i < entries.Count)
                    AppendLog(string.Format(LocalizationService.T(Str.Log_UrlCheckItem), ok ? "✓" : "✗", entries[i].Name));
            });
            var tcs = new TaskCompletionSource();
            worker.Completed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (worker.AnyUrlDiscovered || worker.AnyStreakChanged) { _db.Save(); AppendLog(LocalizationService.T(Str.Log_DbNewSourcesSaved)); }
                IsBusy = false;
                UrlCheckActive = false; UrlCheckPercent = 100;
                int ok = entries.Count(e => e.UrlOk); int nok = entries.Count(e => e.UrlChecked && !e.UrlOk);
                UrlCheckStatus = string.Format(LocalizationService.T(Str.Log_UrlCheckSummaryStatus), ok, nok);
                ApplyFilter();
                tcs.TrySetResult();
            });
            await worker.RunAsync().ConfigureAwait(true);
            await tcs.Task.ConfigureAwait(true);
        }

        /// <summary>Orchestriert beide Phasen des Start-Checks (Versionscheck, dann URL-Check) für
        /// StartupCheckDialog — Nutzerwunsch (2026-09-16): "URLs prüfen" soll im selben
        /// Bitte-warten-Fenster mit eigenem Fortschritt durchlaufen, nicht nur der Versionscheck.
        /// Sequenziell statt parallel: beide Worker würden sonst um dieselben HttpClient-
        /// Verbindungen/Netzwerkbandbreite konkurrieren, ohne dass der Anwender einen Vorteil davon
        /// hätte (das Fenster blockiert ohnehin bis beide fertig sind).</summary>
        public event Action? StartupChecksCompleted;
        public async Task RunStartupChecksAsync()
        {
            await TriggerAutoVersionCheckAsync().ConfigureAwait(true);
            await CheckUrlsAsync().ConfigureAwait(true);
            StartupChecksCompleted?.Invoke();
        }

        /// <summary>Windows-Pendant: MainViewModel.VerifyStickIntegrityAsync() (ViewModels/
        /// MainViewModel.cs ~Zeile 736) — nutzt dieselben plattformneutralen Bausteine
        /// (UsbService.ScanStickVerifiedAsync, IsoEntry.ComputeSha256Async/Sha256/
        /// HashMismatchDetected). Vereinfacht um das dort vorhandene Stick-Update-Angebot
        /// (IncompleteIsosOnStickDetected) und den separaten ActivityHistory-Verlauf — Linux nutzt
        /// dafür bereits LogEntries/IntegrityStatus (siehe Phase A/B-1). Reagiert nicht auf
        /// Abbruch (kein Cancel-Button in Phase A/B — Windows' Abbruch-Zweig daher bewusst
        /// weggelassen, kein Verhaltensunterschied für den Normalfall).</summary>
        public async Task VerifyStickIntegrityAsync()
        {
            if (SelectedDrive?.MountPoint is null || IsBusy) return;
            string mountPoint = SelectedDrive.MountPoint;
            string deviceNode = SelectedDrive.DeviceNode;

            IsBusy = true;
            DownloadPercent = 0;
            IntegrityStatus = LocalizationService.T(Str.Log_CheckingIntegrity);
            AppendLog(string.Format(LocalizationService.T(Str.Log_IntegrityCheckStarted), deviceNode));
            try
            {
                // UsbService.Instance (Core, plattformneutral) — nicht das Linux-eigene _usbService-
                // Feld (LinuxUsbService, nur lsblk-Geräteliste). Analog zu UsbService.UpdateVentoyMenu
                // (statisch) bereits in CopySelectedToStickAsync genutzt.
                var (found, _) = await UsbService.Instance.ScanStickVerifiedAsync(mountPoint, _db.Entries).ConfigureAwait(true);
                var byFilename = new Dictionary<string, UsbService.StickIso>(StringComparer.OrdinalIgnoreCase);
                foreach (UsbService.StickIso f in found)
                    if (!byFilename.ContainsKey(f.Filename)) byFilename[f.Filename] = f;

                int totalToCheck = _db.Entries.Count(e => !string.IsNullOrEmpty(e.Sha256) && byFilename.ContainsKey(e.Filename));
                int mismatches = 0, checkedCount = 0;
                foreach (IsoEntry entry in _db.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Sha256) || !byFilename.TryGetValue(entry.Filename, out var stick)) continue;
                    checkedCount++;
                    DownloadPercent = totalToCheck > 0 ? (checkedCount * 100) / totalToCheck : 0;
                    string actual = await IsoEntry.ComputeSha256Async(stick.FullPath).ConfigureAwait(true);
                    if (string.IsNullOrEmpty(actual)) continue;
                    bool mismatch = !string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase);
                    entry.HashMismatchDetected = mismatch;
                    if (mismatch) mismatches++;
                }

                IntegrityStatus = mismatches > 0
                    ? string.Format(LocalizationService.T(Str.Log_HashMismatchesStatus), mismatches)
                    : string.Format(LocalizationService.T(Str.Log_IsosVerifiedStatus), checkedCount);
                AppendLog(string.Format(LocalizationService.T(Str.Log_IntegrityCheckDone), deviceNode, checkedCount, mismatches));
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(LocalizationService.T(Str.Log_IntegrityCheckFailed), ex.Message));
                IntegrityStatus = LocalizationService.T(Str.Log_ErrorStatus);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string? _lastScannedDeviceNode;

        /// <summary>Nutzerfund (2026-09-04): "automatische USB-Stick-Erkennung" fehlte — bisher
        /// musste der Stick manuell aus dem Dropdown gewählt werden, "Auf dem Stick" blieb immer
        /// "-" (siehe LinuxIsoRow.UsbStatus-Kommentar). Wählt jetzt automatisch einen erkannten
        /// Stick aus (bevorzugt einen bereits eingerichteten Ventoy-Stick) und löst bei jedem
        /// NEU ausgewählten Ventoy-Stick automatisch einen Scan aus (Windows-Pendant:
        /// OnNewDriveInserted → ScanUsbStickAsync). Bewusst NICHT Teil dieser Phase (spätere
        /// Mini-Phasen, siehe Brainstorming): automatisches Ventoy-Einrichten bei Nicht-Ventoy-
        /// Sticks, "veraltet gefunden → automatisch nachkopieren"-Angebot, sowie die drei
        /// Windows-Dialoge für unbekannte/doppelte/neuere Stick-ISOs.</summary>
        /// <summary>Diagnose-Feld (Nutzerfund 2026-09-06: "USB-Scan zeigt vorhandene Distros nicht
        /// an", IsBusy-Absicherung in DownloadQueueAsync hat es NICHT behoben) — merkt sich, für
        /// welchen DeviceNode zuletzt ein Diagnose-Log geschrieben wurde, damit nicht bei jedem
        /// 8s-Poll erneut geloggt wird, nur beim ERSTEN Erkennen/bei einer Änderung.</summary>
        private string? _lastLoggedDeviceNode;

        public async Task PollDrivesAsync()
        {
            List<LinuxBlockDevice> current;
            try { current = await _usbService.ListRemovableDevicesAsync().ConfigureAwait(true); }
            catch (Exception ex)
            {
                // Nutzerfund (2026-09-06): kein einziger Stick-Scan-Protokolleintrag mehr, obwohl
                // ein Ventoy-Stick sichtbar eingesteckt war. Bisher schlug ein Fehler hier (z.B.
                // lsblk nicht im PATH der laufenden App, JSON-Parse-Fehler) lautlos fehl — der
                // 8s-Timer in App.axaml.cs hat KEIN eigenes try/catch, eine Exception hier hätte
                // JEDEN künftigen Tick genauso treffen können, ohne dass irgendwo eine Zeile davon
                // im Protokoll auftaucht. Jetzt sichtbar, damit der nächste Testlauf die eigentliche
                // Ursache zeigt statt weiter zu raten.
                AppendLog($"⚠ USB-Geräteliste konnte nicht gelesen werden: {ex.Message}");
                return;
            }
            string? selectedNode = SelectedDrive?.DeviceNode;

            Drives.Clear();
            foreach (var d in current) Drives.Add(d);

            SelectedDrive = selectedNode is null
                ? Drives.FirstOrDefault(d => d.IsVentoyInstalled) ?? Drives.FirstOrDefault()
                : Drives.FirstOrDefault(d => d.DeviceNode == selectedNode);

            if (SelectedDrive is null) { _lastScannedDeviceNode = null; return; }

            if (SelectedDrive.DeviceNode != _lastLoggedDeviceNode)
            {
                _lastLoggedDeviceNode = SelectedDrive.DeviceNode;
                AppendLog($"ℹ Wechseldatenträger ausgewählt: {SelectedDrive.DeviceNode}, Mountpoint={SelectedDrive.MountPoint ?? "(keiner)"}, Ventoy={SelectedDrive.IsVentoyInstalled}");
            }

            if (SelectedDrive.IsVentoyInstalled && SelectedDrive.MountPoint is not null
                && SelectedDrive.DeviceNode != _lastScannedDeviceNode && !UsbScanActive && !IsBusy)
            {
                _lastScannedDeviceNode = SelectedDrive.DeviceNode;
                _ = ScanConnectedStickAsync(SelectedDrive.MountPoint);
            }
        }

        /// <summary>Windows-Pendant: MainViewModel.ScanUsbStickAsync()/ApplyStickResults() —
        /// hier bewusst nur der Kernteil (Ok/Veraltet/Fehlend pro Eintrag), siehe PollDrivesAsync-
        /// Kommentar für die absichtlich zurückgestellten Teile.</summary>
        private async Task ScanConnectedStickAsync(string mountPoint)
        {
            UsbScanActive = true;
            try
            {
                var (found, incomplete) = await UsbService.Instance.ScanStickVerifiedAsync(mountPoint, _db.Entries).ConfigureAwait(true);
                _lastStickListing = found;
                ApplyStickResults(found);
                await ClassifyStickFindings(found, mountPoint).ConfigureAwait(true);
                ApplyFilter();
                // Diagnose (Nutzerfund 2026-09-06): bisher loggte diese Methode im Erfolgsfall GAR
                // NICHTS — "keine weitere Zeile im Protokoll" ließ sich dadurch nicht von "Scan lief
                // nie" unterscheiden. Jetzt sichtbar: wie viele Dateien gefunden wurden und wie
                // viele Katalog-Einträge dadurch als "Ok" auf dem Stick markiert wurden.
                int okCount = _db.Entries.Count(e => e.UsbStatus == Core.Models.UsbStatus.Ok);
                AppendLog($"ℹ Stick-Scan abgeschlossen: {found.Count} Datei(en) gefunden, {incomplete.Count} unvollständig, {okCount} Katalog-Einträge als 'Ok' markiert.");
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(LocalizationService.T(Str.Linux_Log_StickScanFailed), ex.Message));
            }
            finally
            {
                UsbScanActive = false;
            }
        }

        private void ApplyStickResults(IReadOnlyList<UsbService.StickIso> found)
        {
            var byFn = new Dictionary<string, UsbService.StickIso>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in found) if (!byFn.ContainsKey(f.Filename)) byFn[f.Filename] = f;
            foreach (var e in _db.Entries)
            {
                if (!string.IsNullOrEmpty(e.Filename) && byFn.TryGetValue(e.Filename, out var exact))
                { e.UsbStatus = Core.Models.UsbStatus.Ok; e.UsbSize = FormatGb(exact.Size); continue; }
                var other = found.FirstOrDefault(f => DistroMatcher.IsSameDistroDifferentVersion(e.Filename, f.Filename));
                if (other is not null) { e.UsbStatus = Core.Models.UsbStatus.Outdated; e.UsbSize = FormatGb(other.Size); }
                else { e.UsbStatus = Core.Models.UsbStatus.Missing; e.UsbSize = string.Empty; }
            }
        }

        // ── Stufe 3 der automatischen Stick-Erkennung (Nutzerwunsch 2026-09-08, "unbekannte/
        // doppelte/neuere ISOs"). Windows-Pendant: MainViewModel.ProcessStickScanResults() —
        // bewusst vereinfacht: Windows unterscheidet zusätzlich einen "Versionscheck-Kontext"-Pfad
        // (oldFn-Dictionary aus TriggerAutoVersionCheck) von einem kontextfreien Fallback
        // (DistroMatcher.FindKnownDistroForStickFile). Hier wird IMMER nur der kontextfreie Pfad
        // genutzt — deckt exakt dieselben drei Fälle ab (neuer/dupliziert/unbekannt), nur ohne die
        // Optimierung "schon über den Versionscheck erkannt, nicht doppelt melden". "Missing on
        // Stick" (lokal vollständig, aber nicht auf dem gerade gescannten Stick) bewusst NICHT
        // Teil dieser Phase, siehe MainWindow.axaml.cs RunLocalFileMaintenanceAsync-Kommentar. ──
        // BUGFIX (Nutzerfund 2026-09-16): waren Action-Events — ClassifyStickFindings feuerte alle
        // drei sofort nacheinander ab, OHNE auf den jeweils geöffneten Dialog zu warten ("async
        // void"-Handler geben die Kontrolle beim ersten await sofort zurück). Ergebnis: mehrere
        // Stick-Dialoge (unbekannte ISOs, veraltete Duplikate, neuere Version) poppten gleichzeitig/
        // durcheinander auf, zusätzlich parallel zum Start-Check-Fenster (siehe App.axaml.cs). Jetzt
        // Func<...,Task> — der Aufrufer awaitet jeden Dialog, bevor der nächste Fund gemeldet wird.
        public event Func<List<(IsoEntry DbEntry, UsbService.StickIso StickIso)>, string, Task>? NewerVersionsOnStickDetected;
        public event Func<List<UsbService.StickIso>, string, Task>? UnknownIsosOnStickDetected;
        public event Func<List<(IsoEntry Entry, string OldFilename)>, string, Task>? StaleDuplicatesOnStickDetected;

        private readonly HashSet<string> _newerVersionOfferedKeys = new();
        private readonly HashSet<string> _unknownStickIsoOfferedKeys = new();
        public bool MarkNewerVersionOffered(string drive, string filename) => _newerVersionOfferedKeys.Add($"{drive}|{filename}");
        public bool MarkUnknownStickIsoOffered(string drive, string filename) => _unknownStickIsoOfferedKeys.Add($"{drive}|{filename}");

        private async Task ClassifyStickFindings(IReadOnlyList<UsbService.StickIso> found, string mountPoint)
        {
            var dbFn = new HashSet<string>(_db.Entries.Select(e => e.Filename).Where(f => !string.IsNullOrEmpty(f)), StringComparer.OrdinalIgnoreCase);
            var unmatched = found.Where(f => !string.IsNullOrWhiteSpace(f.Filename) && !dbFn.Contains(f.Filename)).ToList();

            var newer = new List<(IsoEntry, UsbService.StickIso)>();
            var duplicates = new List<(IsoEntry, string)>();
            var trueUnknowns = new List<UsbService.StickIso>();
            foreach (var f in unmatched)
            {
                var match = DistroMatcher.FindKnownDistroForStickFile(_db.Entries, f.Filename);
                if (match is null) trueUnknowns.Add(f);
                else if (match.Value.StickIsNewer) newer.Add((match.Value.Entry, f));
                else duplicates.Add((match.Value.Entry, f.Filename));
            }

            var freshDuplicates = duplicates; // Löschen macht sie beim nächsten Scan von selbst verschwinden — kein Mark-Offered nötig, siehe Windows-Pendant OnStaleDuplicatesOnStick.
            if (freshDuplicates.Count > 0 && StaleDuplicatesOnStickDetected is not null)
                await StaleDuplicatesOnStickDetected(freshDuplicates, mountPoint).ConfigureAwait(true);

            var freshNewer = newer.Where(n => MarkNewerVersionOffered(mountPoint, n.Item2.Filename)).ToList();
            if (freshNewer.Count > 0 && NewerVersionsOnStickDetected is not null)
                await NewerVersionsOnStickDetected(freshNewer, mountPoint).ConfigureAwait(true);

            var freshUnknowns = trueUnknowns.Where(u => MarkUnknownStickIsoOffered(mountPoint, u.Filename)).ToList();
            if (freshUnknowns.Count > 0 && UnknownIsosOnStickDetected is not null)
                await UnknownIsosOnStickDetected(freshUnknowns, mountPoint).ConfigureAwait(true);
        }

        /// <summary>Windows-Pendant: MainViewModel.ReplaceEntryVersion() — übernimmt den neuen
        /// Stick-Dateinamen in einen bestehenden Katalog-Eintrag (kein Duplikat).</summary>
        public void ReplaceEntryVersion(IsoEntry e, string newFn)
        {
            string oldFn = e.Filename;
            string newVer = HttpService.ExtractVersion(newFn);
            e.Filename = newFn; e.RemoteVersion = string.Empty; e.RemoteUrl = string.Empty;
            e.RemoteFilename = string.Empty; e.UpdateAvailable = false;
            // BUGFIX: siehe ApplyResolvedUpdates oben -- fuer die Textersetzung im Namen die dort
            // tatsaechlich vorkommende Version verwenden (nicht die aus dem alten Dateinamen, die
            // z.B. bei CachyOS/EndeavourOS in einem anderen Format vorliegt und im Namen nie
            // gefunden wuerde).
            string nameVer = HttpService.ExtractVersion(e.Name);
            if (!string.IsNullOrEmpty(nameVer) && !string.IsNullOrEmpty(newVer) && nameVer != newVer)
            {
                int pos = e.Name.IndexOf(nameVer, StringComparison.Ordinal);
                if (pos >= 0) e.Name = e.Name[..pos] + newVer + e.Name[(pos + nameVer.Length)..];
            }
            AppendLog(string.Format(LocalizationService.T(Str.Log_FilenameReplaced), e.Name, oldFn, newFn));
            _db.Save();
        }

        /// <summary>Windows-Pendant: MainViewModel.AddEntryFromStickVersion() — legt einen NEUEN,
        /// vom bestehenden Eintrag unabhängigen Katalog-Eintrag für die Stick-Version an.</summary>
        public IsoEntry AddEntryFromStickVersion(IsoEntry src, UsbService.StickIso si)
        {
            var e = new IsoEntry { Name = src.Name, Category = src.Category, Filename = si.Filename, GithubRepo = src.GithubRepo, GithubAsset = src.GithubAsset, Tip = src.Tip, ImportedFromStick = true };
            _db.Add(e);
            AppendLog(string.Format(LocalizationService.T(Str.Log_EntryAddedSimple), e.Name, e.Filename));
            _db.Save();
            return e;
        }

        private static string FormatGb(long bytes) => $"{bytes / 1_073_741_824.0:F2} GB";
    }
}
