using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ULM.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Repair an ISO source with editable metadata and selectable web search results.</summary>
    public sealed class ManualSourceSearchDialog : Window
    {
        private readonly IsoEntry _entry;
        private readonly TextBox _tbName, _tbUrl, _tbFilename, _tbMirror1, _tbMirror2, _tbMirror3,
            _tbGhRepo, _tbGhAsset, _tbTip, _tbTipEn;
        private readonly ComboBox _cbCat;
        private readonly TextBlock _errorTb;
        private readonly TextBox _search;
        private readonly Button _searchButton;
        private readonly StackPanel _results;
        private readonly TextBlock _searchStatus;
        private bool _closed;
        private static string Text(string de, string en) => LocalizationService.Current == AppLanguage.German ? de : en;
        internal static string BuildBrowserSearchUrl(string query) => "https://duckduckgo.com/?q=" + Uri.EscapeDataString(query + " download");

        public ManualSourceSearchDialog(IsoEntry entry)
        {
            _entry = entry;
            Title = LocalizationService.T(Str.Dl_Btn_ManualSearch) + ": " + entry.Name;
            MaxHeight = Math.Max(320, (Screens.Primary?.WorkingArea.Height ?? 800) - 60);
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

            _search = new TextBox { Text = entry.Name + " iso", MinWidth = 240 };
            _searchButton = new Button { Content = Text("Suchen", "Search"), Classes = { "primary" }, Margin = new Thickness(8, 0, 0, 0) };
            var searchRow = new DockPanel { Margin = new Thickness(0, 12, 0, 8) };
            DockPanel.SetDock(_searchButton, Dock.Right);
            searchRow.Children.Add(_searchButton);
            searchRow.Children.Add(_search);
            root.Children.Add(searchRow);
            _searchStatus = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            _results = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            root.Children.Add(_searchStatus);
            root.Children.Add(_results);
            _searchButton.Click += async (_, _) => await SearchAsync();
            Closed += (_, _) => _closed = true;

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

        private async Task SearchAsync()
        {
            string query = _search.Text?.Trim() ?? "";
            if (query.Length == 0 || !_searchButton.IsEnabled) return;
            _searchButton.IsEnabled = false;
            _results.Children.Clear();
            _searchStatus.Text = Text("Suche läuft …", "Searching …");
            try
            {
                var hits = await HttpService.Instance.SearchIsoLinksAsync(query);
                if (_closed) return;
                if (hits.Count == 0)
                {
                    Process.Start(new ProcessStartInfo(BuildBrowserSearchUrl(query)) { UseShellExecute = true });
                    _searchStatus.Text = Text("Keine Treffer in ULM. Browser-Suche geöffnet; gefundene URL oben eintragen.", "No results in ULM. Browser search opened; paste the source URL above.");
                    return;
                }
                _searchStatus.Text = Text($"{hits.Count} Treffer – zum Übernehmen auswählen:", $"{hits.Count} results – select to use:");
                foreach (var hit in hits)
                {
                    var button = new Button
                    {
                        Content = new TextBlock { Text = hit.Filename + " — " + hit.SourcePage, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Classes = { "ghost" }, Margin = new Thickness(0, 0, 0, 4)
                    };
                    button.Click += (_, _) => { _tbUrl.Text = hit.Url; _tbFilename.Text = hit.Filename; };
                    _results.Children.Add(button);
                }
            }
            catch (Exception ex)
            {
                if (!_closed) _searchStatus.Text = Text("Suche/Browser fehlgeschlagen: ", "Search/browser failed: ") + ex.Message;
            }
            finally { if (!_closed) _searchButton.IsEnabled = true; }
        }

        private void TrySave()
        {
            string newName = _tbName.Text?.Trim() ?? string.Empty;
            string newFilename = _tbFilename.Text?.Trim() ?? string.Empty;
            string? error = IsoEditDialog.ValidateEntry(newName, newFilename, _entry, IsoDatabaseServiceAccessor.CurrentEntries());
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

    }
}
