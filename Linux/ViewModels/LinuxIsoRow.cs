using System.ComponentModel;
using System.Text;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.ViewModels
{
    /// <summary>Anzeige-Wrapper: übersetzt IsoEntry-Rohdaten in fertig formatierte UI-Strings.
    /// Trägt zusätzlich den Checkbox-Auswahlzustand für die Zeile (Windows-Pendant: "Haken = Download"
    /// in der Distributionsspalte, siehe MainViewModel.IsSelected auf IsoEntryViewModel).</summary>
    public sealed class LinuxIsoRow : INotifyPropertyChanged
    {
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushMid    = new SolidColorBrush(Color.Parse("#B8C9DC"));
        private static readonly IBrush BrushDim    = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushGreen  = new SolidColorBrush(Color.Parse("#2ECC71"));
        private static readonly IBrush BrushAmber  = new SolidColorBrush(Color.Parse("#F39C12"));
        private static readonly IBrush BrushRed    = new SolidColorBrush(Color.Parse("#E74C3C"));
        private static readonly IBrush BrushTeal   = new SolidColorBrush(Color.Parse("#1ABC9C"));

        private readonly string _downloadDirectory;

        public LinuxIsoRow(IsoEntry entry, string downloadDirectory)
        {
            Entry = entry;
            _downloadDirectory = downloadDirectory;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IsoEntry Entry { get; }

        /// <summary>Windows-Pendant: IsoEntryViewModel.BuildDisplayName() (ViewModels/
        /// IsoViewModels.cs) — Nutzerfund (2026-09-04): "bei URLs prüfen fehlt die Bestätigung
        /// für die einzelnen Distros mit einem grünen Häkchen". 📥-Importiert-Präfix und
        /// 🆕-Update-Tag kostenlos mitgenommen (dieselben IsoEntry-Felder sind bereits über den
        /// automatischen Start-Scan befüllt), NICHT mitgenommen: der laufende Download-Status-
        /// Suffix (eigene Statuszeilen auf Linux, siehe DownloadStatus/StatusBarText).</summary>
        public string Name
        {
            get
            {
                string prefix = Entry.ImportedFromStick ? "📥 " : string.Empty;
                string urlTag = Entry.UrlChecked ? (Entry.UrlOk ? " 🌐✓" : " 🌐✗") : string.Empty;
                string verTag = Entry.HasResolvedUpdate ? $"  🆕 v{Entry.RemoteVersion}" : string.Empty;
                return $"{prefix}{Entry.Name}{urlTag}{verTag}";
            }
        }

        /// <summary>Windows-Pendant: IsoEntryViewModel.TipTooltip (ViewModels/IsoViewModels.cs) —
        /// Nutzerfund (2026-09-08): "Mouse-over-Fenster fehlen" in der Hauptliste. Erklärt alle
        /// aktuell sichtbaren Symbole (📥/🌐✓/🌐✗/🆕) und zeigt danach die Distro-Beschreibung.
        /// Gibt null zurück statt eines leeren Tooltips, wenn nichts davon vorhanden ist.</summary>
        public string? TipTooltip
        {
            get
            {
                var sb = new StringBuilder();
                if (Entry.ImportedFromStick) sb.AppendLine(LocalizationService.T(Str.Row_TipImported));
                if (Entry.UrlChecked) sb.AppendLine(Entry.UrlOk ? LocalizationService.T(Str.Row_TipUrlOk) : LocalizationService.T(Str.Row_TipUrlFail));
                if (Entry.HasResolvedUpdate) sb.AppendLine(string.Format(LocalizationService.T(Str.Row_TipNewVersion), Entry.RemoteVersion));
                bool hasSymbols = sb.Length > 0;

                string description = LocalizationService.Current == AppLanguage.English && !string.IsNullOrWhiteSpace(Entry.TipEn)
                    ? Entry.TipEn : Entry.Tip;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    if (hasSymbols) sb.AppendLine("─────────────────────────");
                    sb.Append(description);
                }

                string result = sb.ToString().Trim();
                return string.IsNullOrEmpty(result) ? null : result;
            }
        }

        /// <summary>Windows-Pendant: IsoEntryViewModel.GetForeground().</summary>
        public IBrush ForegroundBrush
        {
            get
            {
                bool isLocal = Entry.IsLocallyAvailable(_downloadDirectory);
                if (Entry.ImportedFromStick) return BrushTeal;
                if (Entry.UsbStatus == Core.Models.UsbStatus.Ok) return BrushGreen;
                if (Entry.HasResolvedUpdate || Entry.UsbStatus == Core.Models.UsbStatus.Outdated) return BrushAmber;
                if (Entry.UrlChecked && !Entry.UrlOk) return BrushRed;
                if (!Entry.UrlChecked && string.IsNullOrEmpty(Entry.Url) && string.IsNullOrEmpty(Entry.GithubRepo)) return BrushDim;
                if (isLocal) return BrushGreen;
                if (Entry.HasOnlineVersionInfo) return BrushMid;
                return BrushHeader;
            }
        }

        public string CategoryKey => Entry.Category;
        public string CategoryLabel => Constants.CategoryLabel(Entry.Category);

        /// <summary>Zeilen-Checkbox — markiert den Eintrag für den nächsten Sammel-Download
        /// (Windows-Pendant: IsoEntryViewModel.IsSelected, ViewModels/IsoViewModels.cs). Delegiert
        /// direkt an Entry.IsSelected statt an ein eigenes lokales Feld — Nutzerfund (2026-09-04):
        /// ein lokales Row-Feld blieb von DownloadQueueAsync() unsichtbar (das liest
        /// _db.Entries.Where(e => e.IsSelected)), Checkbox-Haken führten deshalb nie zu einer
        /// Warteschlange ("Bitte mindestens eine Distro markieren" trotz Haken). Delegieren löst
        /// das UND übersteht ApplyFilter()-Neuaufbauten (frische LinuxIsoRow-Instanz liest den
        /// Zustand einfach wieder vom Entry), wo ein lokales Feld verloren gegangen wäre.</summary>
        public bool IsSelected
        {
            get => Entry.IsSelected;
            set { if (Entry.IsSelected == value) return; Entry.IsSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

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

        public string StatusLabel => LocalStatus;

        private string? _liveStatus;
        private bool _canRequestFasterMirror;

        /// <summary>Windows-Spalte "Lokal" — zeigt während eines laufenden Download/Kopier-
        /// Vorgangs (siehe LinuxMainViewModel.UpdateRowLiveStatus) den Live-Fortschritt statt des
        /// statischen Lokal/Nicht-lokal-Texts. Absichtlich ein Overlay-Feld statt Entry-Mutation:
        /// vermeidet, dass jeder 0,4s-Fortschritts-Tick des DownloadWorker/CopyToUsbWorker die
        /// persistierte IsoEntry-DB unnötig anfasst.</summary>
        public string LocalStatus => _liveStatus ?? (Entry.IsLocallyAvailable(_downloadDirectory)
            ? $"{LocalizationService.T(Str.Row_Local)} {Entry.LocalFileSize(_downloadDirectory) / 1_048_576} MB"
            : LocalizationService.T(Str.Row_NotLocal));

        /// <summary>Steuert die Sichtbarkeit des "⚡ schneller"-Buttons je Zeile — Windows-Pendant:
        /// DownloadSlotArgs.CanRequestFasterMirror im DownloadProgressDialog.</summary>
        public bool CanRequestFasterMirror => _canRequestFasterMirror;

        /// <summary>Setzt/löscht den Live-Fortschritts-Overlay. <paramref name="status"/> null
        /// löscht ihn wieder (fällt zurück auf den statischen Lokal-Text) — wird nach Abschluss
        /// eines Downloads/einer Kopie NICHT explizit aufgerufen, da ApplyFilter() am Ende ohnehin
        /// alle Rows neu erzeugt (frische Instanz, kein Overlay).</summary>
        public void SetLiveStatus(string? status, bool canRequestFasterMirror)
        {
            if (_liveStatus == status && _canRequestFasterMirror == canRequestFasterMirror) return;
            _liveStatus = status;
            _canRequestFasterMirror = canRequestFasterMirror;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRequestFasterMirror)));
        }

        /// <summary>Windows-Spalte "Auf dem Stick" — inkl. Größenangabe wie unter Windows
        /// ("Ja 3,56 GB"/"Veraltet 3,20 GB"), befüllt durch LinuxMainViewModel.ApplyStickResults()
        /// nach jedem automatischen Stick-Scan.</summary>
        public string UsbStatus => Entry.UsbStatus switch
        {
            Core.Models.UsbStatus.Ok       => string.IsNullOrEmpty(Entry.UsbSize) ? LocalizationService.T(Str.Row_Yes) : $"{LocalizationService.T(Str.Row_Yes)} {Entry.UsbSize}",
            Core.Models.UsbStatus.Outdated => string.IsNullOrEmpty(Entry.UsbSize) ? LocalizationService.T(Str.Row_Outdated) : $"{LocalizationService.T(Str.Row_Outdated)} {Entry.UsbSize}",
            Core.Models.UsbStatus.Missing  => LocalizationService.T(Str.Row_No),
            _                               => LocalizationService.T(Str.Row_Unverified),
        };

        public string VersionStatus
        {
            get
            {
                if (Entry.HasResolvedUpdate) return $"{LocalizationService.T(Str.Row_UpdatePrefix)} v{Entry.RemoteVersion}";
                if (Entry.HasOnlineVersionInfo) return $"{LocalizationService.T(Str.Row_CurrentPrefix)} (v{Entry.RemoteVersion})";
                if (Entry.UsbStatus == Core.Models.UsbStatus.Ok) return LocalizationService.T(Str.Row_Yes);
                if (Entry.IsLocallyAvailable(_downloadDirectory)) return LocalizationService.T(Str.Row_LocallyAvailable);
                return "?";
            }
        }

        public string ManualSearchTooltip => LocalizationService.T(Str.Row_ManualSearchTooltip);
        public bool ShowManualSearchButton => Entry.FailedResolveStreak >= Constants.ManualSearchFailureThreshold;
        public bool HasHashStatus => !string.IsNullOrEmpty(Entry.Sha256);
        public IBrush HashStatusBrush => Entry.HashMismatchDetected ? BrushRed : BrushGreen;
        public string HashStatusTooltip => Entry.HashMismatchDetected
            ? LocalizationService.T(Str.Row_HashMismatch)
            : Entry.Sha256Source == "OfficialChecksum"
                ? LocalizationService.T(Str.Row_HashVerifiedOfficial)
                : LocalizationService.T(Str.Row_HashLocalOnly);

        /// <summary>Meldet Anzeige-Properties als geändert, ohne das Objekt neu zu erzeugen —
        /// nötig, weil UsbStatus/VersionStatus vom gemeinsamen IsoEntry gelesen werden, das sich
        /// z.B. nach einem Download-Resolve ändert, ohne dass ApplyFilter() neu aufgerufen wird.</summary>
        public void RaiseDisplayChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TipTooltip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ForegroundBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsbStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ManualSearchTooltip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowManualSearchButton)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHashStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HashStatusBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HashStatusTooltip)));
        }
    }
}
