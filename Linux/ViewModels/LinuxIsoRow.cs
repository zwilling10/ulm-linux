using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.ViewModels
{
    /// <summary>Anzeige-Wrapper: übersetzt IsoEntry-Rohdaten in fertig formatierte UI-Strings.</summary>
    public sealed class LinuxIsoRow
    {
        private readonly string _downloadDirectory;

        public LinuxIsoRow(IsoEntry entry, string downloadDirectory)
        {
            Entry = entry;
            _downloadDirectory = downloadDirectory;
        }

        public IsoEntry Entry { get; }

        public string Name => Entry.Name;
        public string CategoryKey => Entry.Category;
        public string CategoryLabel => Constants.CategoryLabel(Entry.Category);

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

        public string StatusLabel => Entry.IsLocallyAvailable(_downloadDirectory)
            ? LocalizationService.T(Str.Row_Local)
            : LocalizationService.T(Str.Row_NotLocal);
    }
}
