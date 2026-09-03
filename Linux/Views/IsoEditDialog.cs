using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' IsoEditDialog (WPF, Zeilen
    /// 176-285) — reines Code-behind wie das Original. Mutiert den übergebenen IsoEntry direkt bei
    /// "Speichern" (kein Kopieren), liefert nur ob gespeichert wurde.</summary>
    public sealed class IsoEditDialog : Window
    {
        private readonly IsoEntry _entry;
        private readonly TextBox _tbName, _tbUrl, _tbFilename, _tbMirror1, _tbMirror2, _tbMirror3,
            _tbGhRepo, _tbGhAsset, _tbTip, _tbTipEn;
        private readonly ComboBox _cbCat;
        private readonly TextBlock _errorTb;

        public IsoEditDialog(IsoEntry entry, bool isNew)
        {
            _entry = entry;
            Title = isNew
                ? LocalizationService.T(Str.Db_EditDialog_Title_New)
                : string.Format(LocalizationService.T(Str.Db_EditDialog_Title_Edit), entry.Name);
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var scroll = new ScrollViewer();
            var root = new StackPanel { Margin = new Thickness(20) };

            _tbName     = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Name), entry.Name);
            _cbCat      = DbFieldHelpers.AddCategoryCombo(root, entry.Category);
            _tbUrl      = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_PrimaryUrl), entry.Url);
            _tbFilename = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Filename), entry.Filename);
            _tbMirror1  = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Mirror1), entry.Mirror1);
            _tbMirror2  = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Mirror2), entry.Mirror2);
            _tbMirror3  = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Mirror3), entry.Mirror3);
            _tbGhRepo   = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_GithubRepo), entry.GithubRepo);
            _tbGhAsset  = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_GithubAsset), entry.GithubAsset);
            _tbTip      = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_Description), entry.Tip, multiLine: true);
            _tbTipEn    = DbFieldHelpers.AddField(root, LocalizationService.T(Str.Db_Field_DescriptionEn), entry.TipEn, multiLine: true);

            _errorTb = new TextBlock { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E74C3C")), TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_errorTb);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = LocalizationService.T(Str.Db_Btn_Save), Classes = { "primary" }, MinWidth = 110 };
            ok.Click += (_, _) => TrySave();
            var cancel = new Button { Content = LocalizationService.T(Str.Db_Btn_Cancel), Classes = { "ghost" }, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (_, _) => Close(false);
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            scroll.Content = root;
            Content = scroll;
        }

        private void TrySave()
        {
            string newName = _tbName.Text?.Trim() ?? string.Empty;
            string newFilename = _tbFilename.Text?.Trim() ?? string.Empty;
            string? error = ValidateEntry(newName, newFilename, _entry, IsoDatabaseServiceAccessor.CurrentEntries());
            if (error is not null)
            {
                _errorTb.Text = error;
                _errorTb.IsVisible = true;
                return;
            }

            _entry.Name = newName;
            _entry.Category = DbFieldHelpers.SelectedCategory(_cbCat);
            _entry.Url = _tbUrl.Text?.Trim() ?? string.Empty;
            _entry.Filename = newFilename;
            _entry.Mirror1 = _tbMirror1.Text?.Trim() ?? string.Empty;
            _entry.Mirror2 = _tbMirror2.Text?.Trim() ?? string.Empty;
            _entry.Mirror3 = _tbMirror3.Text?.Trim() ?? string.Empty;
            _entry.GithubRepo = _tbGhRepo.Text?.Trim() ?? string.Empty;
            _entry.GithubAsset = _tbGhAsset.Text?.Trim() ?? string.Empty;
            _entry.Tip = _tbTip.Text?.Trim() ?? string.Empty;
            _entry.TipEn = _tbTipEn.Text?.Trim() ?? string.Empty;
            Close(true);
        }

        /// <summary>Reine Validierungslogik, ohne UI — testbar ohne Avalonia-Fenster zu
        /// instanziieren (Avalonia-Window braucht eine laufende Headless-Plattform, die die
        /// bestehende Testsuite nicht aufsetzt). 1:1 Regeln wie die WPF-Vorlage: Name+Dateiname
        /// Pflicht, Name muss unter allen ANDEREN Einträgen eindeutig sein (siehe dortiger
        /// Bugfix-Kommentar: gleiche Namen verwechseln sonst die Mirror-Verfolgung beim Download).</summary>
        internal static string? ValidateEntry(string name, string filename, IsoEntry currentEntry, IEnumerable<IsoEntry> allEntries)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(filename))
                return LocalizationService.T(Str.Db_RequiredFields_Body);

            bool nameTaken = allEntries.Any(other =>
                !ReferenceEquals(other, currentEntry) && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));
            if (nameTaken)
                return string.Format(LocalizationService.T(Str.Db_NameTaken_Body), name);

            return null;
        }
    }

    /// <summary>Kleiner Indirektionspunkt für IsoDatabaseService.Instance.Entries — hält
    /// IsoEditDialog.ValidateEntry unabhängig vom konkreten Aufrufkontext testbar (Tests rufen
    /// ValidateEntry direkt mit einer eigenen Liste auf, ohne diese Klasse zu berühren).</summary>
    internal static class IsoDatabaseServiceAccessor
    {
        public static IEnumerable<IsoEntry> CurrentEntries() => ULM.Core.Services.IsoDatabaseService.Instance.Entries;
    }
}
