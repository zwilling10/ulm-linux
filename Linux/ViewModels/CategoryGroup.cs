using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ULM.Linux.ViewModels
{
    /// <summary>Eine Kategorie-Gruppe für die ISO-Auswahl-Liste — Avalonia-Äquivalent zu Windows'
    /// "CategoryTemplate" (Card-Header mit 3-State-Sammel-Checkbox + darunterliegende Zeilen).
    /// Ersetzt die frühere Linux-Sidebar-Kategorienavigation: statt einer Auswahl, die den Rest
    /// ausblendet, zeigt die Liste jetzt IMMER alle Kategorien mit ihren Einträgen, genau wie
    /// unter Windows.</summary>
    public sealed class CategoryGroup : INotifyPropertyChanged
    {
        public CategoryGroup(string categoryLabel, IEnumerable<LinuxIsoRow> entries)
        {
            CategoryLabel = categoryLabel;
            Entries = new ObservableCollection<LinuxIsoRow>(entries);
            foreach (LinuxIsoRow row in Entries)
                row.PropertyChanged += OnEntryPropertyChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CategoryLabel { get; }
        public ObservableCollection<LinuxIsoRow> Entries { get; }

        private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinuxIsoRow.IsSelected))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllSelected)));
        }

        /// <summary>Tri-State: true = alle ausgewählt, false = keine, null = teilweise —
        /// analog zur Windows-`AllSelected`-Sammel-Checkbox (`IsThreeState="True"`).</summary>
        public bool? AllSelected
        {
            get
            {
                if (Entries.Count == 0) return false;
                bool any = false, allSet = true;
                foreach (LinuxIsoRow row in Entries)
                {
                    if (row.IsSelected) any = true; else allSet = false;
                }
                if (any && allSet) return true;
                if (!any) return false;
                return null;
            }
            set
            {
                bool target = value == true;
                foreach (LinuxIsoRow row in Entries) row.IsSelected = target;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllSelected)));
            }
        }
    }
}
