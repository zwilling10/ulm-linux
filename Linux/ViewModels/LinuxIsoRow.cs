using System.ComponentModel;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.ViewModels
{
    /// <summary>Anzeige-Wrapper: übersetzt IsoEntry-Rohdaten in fertig formatierte UI-Strings.
    /// Trägt zusätzlich den Checkbox-Auswahlzustand für die Zeile (Windows-Pendant: "Haken = Download"
    /// in der Distributionsspalte, siehe MainViewModel.IsSelected auf IsoEntryViewModel).</summary>
    public sealed class LinuxIsoRow : INotifyPropertyChanged
    {
        private readonly string _downloadDirectory;

        public LinuxIsoRow(IsoEntry entry, string downloadDirectory)
        {
            Entry = entry;
            _downloadDirectory = downloadDirectory;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IsoEntry Entry { get; }

        public string Name => Entry.Name;
        public string CategoryKey => Entry.Category;
        public string CategoryLabel => Constants.CategoryLabel(Entry.Category);

        private bool _isSelected;
        /// <summary>Zeilen-Checkbox — markiert den Eintrag für den nächsten Sammel-Download,
        /// analog zur Windows-Spaltenüberschrift "Haken = Download". Phase A führt nur das Feld +
        /// die Bindung ein; ein Mehrfach-Download-Befehl über alle IsSelected-Zeilen ist bewusst
        /// nicht Teil dieser Phase (SelectedRow/DownloadCommand bleiben unverändert Single-Select).</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
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

        /// <summary>Windows-Spalte "Lokal".</summary>
        public string LocalStatus => Entry.IsLocallyAvailable(_downloadDirectory)
            ? LocalizationService.T(Str.Row_Local)
            : LocalizationService.T(Str.Row_NotLocal);

        /// <summary>Windows-Spalte "Auf dem Stick". Nutzt das bereits im geteilten Core-Modell
        /// vorhandene <see cref="IsoEntry.UsbStatus"/>-Feld — auf Linux bislang IMMER
        /// <see cref="UsbStatus.Unknown"/> ("-"), da kein Stick-Scan es befüllt (kein
        /// LinuxUsbService-Äquivalent zum Windows-Katalog-Scan). Absichtlich keine erfundenen
        /// Werte: das Feld zeigt ehrlich "unbekannt", bis eine spätere Phase den Scan nachliefert.</summary>
        public string UsbStatus => Entry.UsbStatus switch
        {
            Core.Models.UsbStatus.Ok       => LocalizationService.T(Str.Row_Yes),
            Core.Models.UsbStatus.Outdated => LocalizationService.T(Str.Row_Yes),
            Core.Models.UsbStatus.Missing  => LocalizationService.T(Str.Row_No),
            _                               => "-",
        };

        /// <summary>Windows-Spalte "Aktuell". Zeigt die zuletzt aufgelöste Version, falls
        /// vorhanden (nach einem Download-Versuch via ResolveFunc gesetzt) — ohne Live-
        /// Update-Vergleich (das ist Phase-B-Scope, "Updates prüfen").</summary>
        public string VersionStatus => string.IsNullOrEmpty(Entry.RemoteVersion) ? "-" : $"v{Entry.RemoteVersion}";

        /// <summary>Meldet Anzeige-Properties als geändert, ohne das Objekt neu zu erzeugen —
        /// nötig, weil UsbStatus/VersionStatus vom gemeinsamen IsoEntry gelesen werden, das sich
        /// z.B. nach einem Download-Resolve ändert, ohne dass ApplyFilter() neu aufgerufen wird.</summary>
        public void RaiseDisplayChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsbStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionStatus)));
        }
    }
}
