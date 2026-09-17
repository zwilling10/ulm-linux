using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' ImportStickIsosDialog (WPF)
    /// — reines Code-behind wie das Original. Zeigt jede unbekannte ISO-Datei auf dem Stick mit
    /// editierbarem Namen, Kategorie und optionaler Quelle-URL.</summary>
    public sealed class ImportStickIsosDialog : Window
    {
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushMid    = new SolidColorBrush(Color.Parse("#B8C9DC"));
        private static readonly IBrush BrushDim    = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#336B9E"));

        public List<(IsoEntry Entry, string SourcePath)> ImportedEntries { get; } = new();

        private sealed class ImportRow
        {
            public required CheckBox Chk = null!;
            public required TextBox NameTb = null!;
            public required ComboBox CatCb = null!;
            public required TextBox UrlTb = null!;
            public required TextBlock SearchStatusTb = null!;
            public required UsbService.StickIso Iso = null!;
            public required string SuggestedName = null!;
        }

        private readonly List<ImportRow> _rows = new();

        public ImportStickIsosDialog(IReadOnlyList<UsbService.StickIso> unknownIsos)
        {
            Title = LocalizationService.T(Str.Db_ImportDialog_Title);
            Width = 660; Height = 520;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12.5,
                Foreground = BrushHeader, Text = string.Format(LocalizationService.T(Str.Db_ImportDialog_Info), unknownIsos.Count),
            });

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list = new StackPanel();

            var colH = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnDefinitions = new ColumnDefinitions("28,*,160") };
            void AddCH(int c, string t)
            {
                var tb = new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, FontSize = 10.5, Foreground = BrushMid };
                Grid.SetColumn(tb, c); colH.Children.Add(tb);
            }
            AddCH(1, LocalizationService.T(Str.Db_ColHeader_NameEdit));
            AddCH(2, LocalizationService.T(Str.Db_ColHeader_Category));
            list.Children.Add(colH);

            foreach (var iso in unknownIsos)
            {
                string suggested = Path.GetFileNameWithoutExtension(iso.Filename).Replace('-', ' ').Replace('_', ' ').Trim();
                var rg = new Grid { Margin = new Thickness(0, 4, 0, 4), ColumnDefinitions = new ColumnDefinitions("28,*,160") };

                var chk = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(chk, iso.Filename);
                var nameTb = new TextBox { Text = suggested, MinHeight = 28, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(4, 0, 4, 0) };
                string defCat = Constants.Categories.Contains(iso.Category) ? iso.Category : "Einsteiger";
                var catCb = new ComboBox { Margin = new Thickness(0) };
                DbFieldHelpers.FillCategoryCombo(catCb, defCat);

                var urlTb = new TextBox { MinHeight = 24, FontSize = 10.5, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(32, 2, 0, 6) };

                chk.IsCheckedChanged += (_, _) =>
                {
                    bool on = chk.IsChecked == true;
                    nameTb.IsEnabled = on; catCb.IsEnabled = on; urlTb.IsEnabled = on;
                };

                Grid.SetColumn(chk, 0); Grid.SetColumn(nameTb, 1); Grid.SetColumn(catCb, 2);
                rg.Children.Add(chk); rg.Children.Add(nameTb); rg.Children.Add(catCb);
                list.Children.Add(rg);
                list.Children.Add(new TextBlock { Text = string.Format(LocalizationService.T(Str.Db_FileSizeLine), iso.Filename, (iso.Size / 1_073_741_824.0).ToString("F2")), FontSize = 9.5, Foreground = BrushDim, Margin = new Thickness(32, 0, 0, 0) });
                list.Children.Add(new TextBlock { Text = LocalizationService.T(Str.Db_SourceUrlLabel), FontSize = 9, Foreground = BrushDim, Margin = new Thickness(32, 4, 0, 0) });
                list.Children.Add(urlTb);
                var statusTb = new TextBlock { FontSize = 9, Foreground = BrushDim, Margin = new Thickness(32, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
                list.Children.Add(statusTb);
                list.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 2), Background = BrushBorder });
                var row = new ImportRow { Chk = chk, NameTb = nameTb, CatCb = catCb, UrlTb = urlTb, SearchStatusTb = statusTb, Iso = iso, SuggestedName = suggested };
                _rows.Add(row);
            }

            scroll.Content = list;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // BUGFIX: Ursprünglich lief die automatische Quellensuche für ALLE Zeilen gleichzeitig
            // parallel los — jede eigene HttpService.ResolveLatestAsync-Kette feuert mehrere
            // DuckDuckGo-Websuchen ab. Mehrere Ketten gleichzeitig vervielfachen die Anfragen an
            // DuckDuckGo innerhalb kürzester Zeit und lösen live beobachtet dessen Bot-/Anomalie-
            // Erkennung aus (Suchergebnis wird durch eine Blockseite ersetzt) — betraf dann auch
            // Zeilen, die einzeln nacheinander durchaus fündig geworden wären. Läuft jetzt für
            // eine Zeile nach der anderen.
            _ = RunAutoSourceSearchSequentialAsync();

            var br = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var bAll = new Button { Content = LocalizationService.T(Str.Db_Btn_SelectAll), Classes = { "ghost" }, Width = 130, Margin = new Thickness(0, 0, 6, 0) };
            bAll.Click += (_, _) => { foreach (var r in _rows) { r.Chk.IsChecked = true; r.NameTb.IsEnabled = r.CatCb.IsEnabled = r.UrlTb.IsEnabled = true; } };
            var bNone = new Button { Content = LocalizationService.T(Str.Db_Btn_DeselectAll), Classes = { "ghost" }, Width = 120, Margin = new Thickness(0, 0, 14, 0) };
            bNone.Click += (_, _) => { foreach (var r in _rows) { r.Chk.IsChecked = false; r.NameTb.IsEnabled = r.CatCb.IsEnabled = r.UrlTb.IsEnabled = false; } };
            var bSkip = new Button { Content = LocalizationService.T(Str.Db_Btn_Skip), Classes = { "ghost" }, Width = 120, Margin = new Thickness(0, 0, 8, 0) };
            bSkip.Click += (_, _) => Close(false);
            var bImp = new Button { Content = LocalizationService.T(Str.Db_Btn_Import), Classes = { "primary" }, Width = 130 };
            bImp.Click += BtnImport_Click;
            br.Children.Add(bAll); br.Children.Add(bNone); br.Children.Add(bSkip); br.Children.Add(bImp);
            Grid.SetRow(br, 2);
            root.Children.Add(br);
            Content = root;
        }

        /// <summary>Löst die Quelle-URL für jede neu gefundene, unbekannte Stick-ISO automatisch
        /// über HttpService.ResolveLatestAsync auf (dieselbe Auflösungskette wie der "🔍 Online
        /// suchen"-Button in IsoEditDialog) — der Nutzer soll die URL beim Import nicht mehr von
        /// Hand heraussuchen müssen. Arbeitet die Zeilen NACHEINANDER ab (nicht parallel), damit
        /// nicht mehrere DuckDuckGo-Websuchen gleichzeitig laufen — siehe Bugfix-Kommentar am
        /// Aufrufer.</summary>
        private async Task RunAutoSourceSearchSequentialAsync()
        {
            foreach (var row in _rows)
                await RunAutoSourceSearchAsync(row).ConfigureAwait(true);
        }

        private async Task RunAutoSourceSearchAsync(ImportRow row)
        {
            row.SearchStatusTb.Text = LocalizationService.T(Str.Db_OnlineSearch_Running);
            row.SearchStatusTb.IsVisible = true;
            try
            {
                var probe = new IsoEntry { Name = row.SuggestedName, Filename = row.Iso.Filename };
                var (ver, url, _) = await HttpService.Instance.ResolveLatestAsync(probe).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(url))
                {
                    row.SearchStatusTb.Text = HttpService.Instance.LastResolveWasSearchEngineBlocked
                        ? LocalizationService.T(Str.Db_OnlineSearch_Blocked)
                        : LocalizationService.T(Str.Db_OnlineSearch_NotFound);
                    return;
                }

                if (string.IsNullOrWhiteSpace(row.UrlTb.Text)) row.UrlTb.Text = url;
                row.SearchStatusTb.Text = string.Format(LocalizationService.T(Str.Db_OnlineSearch_Found), ver);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoSourceSearch] {row.SuggestedName}: {ex.Message}");
                row.SearchStatusTb.Text = LocalizationService.T(Str.Db_OnlineSearch_NotFound);
            }
        }

        private async void BtnImport_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var errors = new List<string>();
            foreach (var row in _rows)
            {
                if (row.Chk.IsChecked != true) continue;
                string name = row.NameTb.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) { errors.Add(string.Format(LocalizationService.T(Str.Db_ImportError_NoName), row.Iso.Filename)); continue; }
                var entry = new IsoEntry { Name = name, Category = DbFieldHelpers.SelectedCategory(row.CatCb), Filename = row.Iso.Filename, Url = row.UrlTb.Text?.Trim() ?? string.Empty, ImportedFromStick = true };
                ImportedEntries.Add((entry, row.Iso.FullPath));
            }
            if (errors.Count > 0)
                await InfoDialog.ShowAsync(this, LocalizationService.T(Str.Db_ImportIncomplete_Title), string.Format(LocalizationService.T(Str.Db_ImportIncomplete_Body), string.Join("\n", errors)));
            Close(ImportedEntries.Count > 0);
        }
    }
}
