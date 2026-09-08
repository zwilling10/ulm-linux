using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' IsoSearchDialog (WPF, Zeilen
    /// 285-459) — reines Code-behind wie das Original. 2 Reiter (Aktuellste/Beliebteste), beides
    /// Online-Abfragen gegen DistroWatch über den bereits plattformneutralen
    /// Core/Services/DiscoveryService.cs.</summary>
    public sealed class IsoSearchDialog : Window
    {
        private static readonly IBrush BrushDim = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushAlreadyInDbBg = new SolidColorBrush(Color.Parse("#16324F"));
        private static readonly IBrush BrushTransparent = Brushes.Transparent;

        public List<IsoEntry> AddedEntries { get; } = new();
        public HashSet<IsoEntry> ToDownload { get; } = new();

        private sealed class DiscoveryRow
        {
            public required Grid Row = null!;
            public required CheckBox Chk = null!;
            public required ComboBox CatCb = null!;
            public required TextBlock NameTb = null!;
            public required DiscoveredDistro Distro = null!;
        }

        private sealed class DiscoveryTab
        {
            public required StackPanel RowsPanel = null!;
            public required TextBlock StatusTb = null!;
            public required CheckBox AlsoDownloadChk = null!;
            public readonly List<DiscoveryRow> Rows = new();
            public bool Loaded;
        }

        private readonly DiscoveryTab _latestTab = MakeTabState();
        private readonly DiscoveryTab _popularTab = MakeTabState();

        private static DiscoveryTab MakeTabState() => new()
        {
            RowsPanel = new StackPanel(),
            StatusTb = new TextBlock { FontSize = 10.5, Foreground = BrushDim, Margin = new Thickness(0, 0, 0, 8) },
            AlsoDownloadChk = new CheckBox { Content = LocalizationService.T(Str.Db_Chk_DownloadImmediately), VerticalAlignment = VerticalAlignment.Center },
        };

        public IsoSearchDialog()
        {
            Title = LocalizationService.T(Str.Db_SearchDialog_Title);
            Width = 640;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,Auto") };

            var tabs = new TabControl();
            tabs.Items.Add(BuildDiscoveryTab(LocalizationService.T(Str.Db_Tab_Latest), _latestTab, forceRefresh => DiscoveryService.Instance.GetLatestAdditionsAsync(forceRefresh)));
            tabs.Items.Add(BuildDiscoveryTab(LocalizationService.T(Str.Db_Tab_Popular), _popularTab, forceRefresh => DiscoveryService.Instance.GetMostPopularAsync(forceRefresh)));
            Grid.SetRow(tabs, 0);

            var closeBtn = new Button { Content = LocalizationService.T(Str.Db_Btn_CloseSimple), Classes = { "ghost" }, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            closeBtn.Click += (_, _) => Close(true);
            Grid.SetRow(closeBtn, 1);

            root.Children.Add(tabs);
            root.Children.Add(closeBtn);
            Content = root;

            // Erster Reiter lädt sofort beim Öffnen, der zweite erst bei seinem ersten Anklicken —
            // kein unnötiger Netzwerk-Roundtrip, falls der Nutzer nur eine der beiden Listen braucht.
            Opened += async (_, _) => { if (!_latestTab.Loaded) await LoadDiscoveryTabAsync(_latestTab, forceRefresh: false, DiscoveryService.Instance.GetLatestAdditionsAsync); };
            tabs.SelectionChanged += async (_, _) =>
            {
                if (tabs.SelectedIndex == 1 && !_popularTab.Loaded) await LoadDiscoveryTabAsync(_popularTab, forceRefresh: false, DiscoveryService.Instance.GetMostPopularAsync);
            };
        }

        private TabItem BuildDiscoveryTab(string header, DiscoveryTab tab, Func<bool, Task<DiscoveryService.DiscoveryResult>> fetch)
        {
            var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*,Auto") };

            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var refreshBtn = new Button { Content = LocalizationService.T(Str.Db_Btn_Refresh), Classes = { "ghost" }, MinWidth = 130 };
            DockPanel.SetDock(refreshBtn, Dock.Right);
            headerRow.Children.Add(refreshBtn);
            headerRow.Children.Add(tab.StatusTb);
            Grid.SetRow(headerRow, 0);

            var scroll = new ScrollViewer { Content = tab.RowsPanel };
            Grid.SetRow(scroll, 1);

            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var takeBtn = new Button { Content = LocalizationService.T(Str.Db_Btn_TakeOver), Classes = { "primary" }, MinWidth = 140 };
            DockPanel.SetDock(takeBtn, Dock.Right);
            takeBtn.Click += (_, _) => TakeSelected(tab);
            footer.Children.Add(takeBtn);
            footer.Children.Add(tab.AlsoDownloadChk);
            Grid.SetRow(footer, 2);

            refreshBtn.Click += async (_, _) => await LoadDiscoveryTabAsync(tab, forceRefresh: true, fetch);

            root.Children.Add(headerRow);
            root.Children.Add(scroll);
            root.Children.Add(footer);
            return new TabItem { Header = header, Content = root };
        }

        private async Task LoadDiscoveryTabAsync(DiscoveryTab tab, bool forceRefresh, Func<bool, Task<DiscoveryService.DiscoveryResult>> fetch)
        {
            tab.StatusTb.Text = LocalizationService.T(Str.Db_Loading);
            tab.RowsPanel.Children.Clear();
            tab.Rows.Clear();
            try
            {
                DiscoveryService.DiscoveryResult result = await fetch(forceRefresh).ConfigureAwait(true);
                tab.Loaded = true;
                if (result.Items.Count == 0)
                {
                    tab.StatusTb.Text = LocalizationService.T(Str.Db_NoDiscoveryResults);
                    return;
                }
                tab.StatusTb.Text = (result.FromCache ? LocalizationService.T(Str.Db_FromCache) : LocalizationService.T(Str.Db_FreshlyLoaded))
                    + string.Format(LocalizationService.T(Str.Db_DiscoveryStatusSuffix), result.FetchedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));

                foreach (DiscoveredDistro d in result.Items)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2), ColumnDefinitions = new ColumnDefinitions("34,*,32,150,Auto") };
                    ApplyRowHighlight(row, d);

                    // Nutzerfund (2026-09-03, Screenshot): Checkbox wirkte "abgeschnitten" — die
                    // Spalte war mit 24px knapper bemessen als Fluents CheckBox-Indikator (~20px)
                    // plus 4px Rand auf jeder Seite tatsächlich braucht (24 - 8 = 16px reichten
                    // nicht). Spalte auf 34px verbreitert, Rand entsprechend verschmälert.
                    var chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2), IsEnabled = !d.AlreadyInDb };
                    Grid.SetColumn(chk, 0);
                    row.Children.Add(chk);

                    var nameTb = new TextBlock
                    {
                        Text = d.AlreadyInDb ? string.Format(LocalizationService.T(Str.Db_NameAlreadyInDb), d.Name) : d.Name,
                        VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 4, 0, 4),
                        Foreground = d.AlreadyInDb ? BrushDim : BrushHeader,
                    };
                    if (!d.AlreadyInDb) ToolTip.SetTip(nameTb, BuildInfoTooltip(d));
                    Grid.SetColumn(nameTb, 1);
                    row.Children.Add(nameTb);

                    // Nutzerfund (2026-09-08, Screenshot): "genau links neben der Kategorie fehlt
                    // der Button" — eigenes Vorschau-Fenster statt/zusätzlich zum Mouseover-Tooltip
                    // (der bei kurzem Antippen auf Touch-/Laptop-Trackpads unpraktisch ist). Zeigt
                    // denselben Inhalt wie BuildInfoTooltip, nur als klick-bares Fenster.
                    var previewBtn = new Button
                    {
                        Content = "👁", Classes = { "ghost" }, Width = 28, Height = 28, Padding = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                    };
                    ToolTip.SetTip(previewBtn, LocalizationService.T(Str.Db_PreviewButtonTooltip));
                    previewBtn.Click += async (_, _) => await InfoDialog.ShowAsync(this, d.Name, BuildInfoTooltip(d));
                    Grid.SetColumn(previewBtn, 2);
                    row.Children.Add(previewBtn);

                    // Nutzerfund (2026-09-03, Screenshot "Beliebteste"-Tab): Kategorie-Dropdowns
                    // wirkten zeilenweise uneinheitlich ausgerichtet — im Gegensatz zu chk/nameTb/
                    // infoTb hier VerticalAlignment=Center gefehlt (Grid-Zellen defaulten auf
                    // Stretch), dadurch streckte sich die ComboBox auf die volle, je nach
                    // Icon-Glyph (⚙ vs. 🖥 vs. 🎮 — unterschiedliche Fallback-Schriften mit
                    // unterschiedlichen Zeilenhöhen) leicht schwankende Zeilenhöhe.
                    var catCb = new ComboBox { Margin = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center, IsEnabled = !d.AlreadyInDb };
                    DbFieldHelpers.FillCategoryCombo(catCb, d.SuggestedCategory);
                    Grid.SetColumn(catCb, 3);
                    row.Children.Add(catCb);

                    var infoTb = new TextBlock { Text = d.Info, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 4, 4), Foreground = BrushDim };
                    Grid.SetColumn(infoTb, 4);
                    row.Children.Add(infoTb);

                    tab.RowsPanel.Children.Add(row);
                    tab.Rows.Add(new DiscoveryRow { Row = row, Chk = chk, CatCb = catCb, NameTb = nameTb, Distro = d });
                }
            }
            catch (Exception ex)
            {
                tab.StatusTb.Text = string.Format(LocalizationService.T(Str.Db_DiscoveryError), ex.Message);
            }
        }

        private void TakeSelected(DiscoveryTab tab)
        {
            bool alsoDownload = tab.AlsoDownloadChk.IsChecked == true;
            int taken = 0;
            foreach (DiscoveryRow row in tab.Rows)
            {
                if (row.Distro.AlreadyInDb || row.Chk.IsChecked != true) continue;
                string category = DbFieldHelpers.SelectedCategory(row.CatCb);
                var entry = new IsoEntry { Name = row.Distro.Name, Category = category };
                AddedEntries.Add(entry);
                if (alsoDownload) ToDownload.Add(entry);

                row.Distro.AlreadyInDb = true;
                row.Chk.IsChecked = false;
                row.Chk.IsEnabled = false;
                row.CatCb.IsEnabled = false;
                row.NameTb.Text = string.Format(LocalizationService.T(Str.Db_NameAlreadyInDb), row.Distro.Name);
                row.NameTb.Foreground = BrushDim;
                ToolTip.SetTip(row.NameTb, null);
                ApplyRowHighlight(row.Row, row.Distro);
                taken++;
            }
            if (taken > 0) tab.StatusTb.Text = string.Format(LocalizationService.T(Str.Db_TakenOverStatus), taken, tab.StatusTb.Text);
        }

        private static void ApplyRowHighlight(Grid row, DiscoveredDistro d) =>
            row.Background = d.AlreadyInDb ? BrushAlreadyInDbBg : BrushTransparent;

        private static string BuildInfoTooltip(DiscoveredDistro d)
        {
            var lines = new List<string> { d.Name, d.Info, string.Format(LocalizationService.T(Str.Db_SuggestedCategory), Constants.CategoryLabel(d.SuggestedCategory)) };
            if (d.Tags.Count > 0) lines.Add(string.Format(LocalizationService.T(Str.Db_DistrowatchTags), string.Join(", ", d.Tags)));
            lines.Add($"distrowatch.com/{d.Slug}");
            return string.Join("\n", lines);
        }
    }
}
