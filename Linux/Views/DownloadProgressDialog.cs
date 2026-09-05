using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Windows' DownloadProgressDialog (Views/Dialogs/
    /// DownloadDialogs.cs) — Nutzerfund (2026-09-04): "es fehlt das Fenster das den Download und
    /// Kopieren anzeigt", die bisherige Inline-Zeilen-Anzeige allein reichte nicht. Bewusst
    /// SCHLANKER als das Windows-Original: kein "🔧 Quelle manuell suchen"-Button (der hängt am
    /// Windows-only Härtefall-Nachschlag-System, das auf Linux nicht existiert) und der
    /// "(schneller)"-Button nutzt einen normalen, per Theme gestylten Button statt Windows'
    /// eigenem ControlTemplate/Trigger-Pillenbutton (Avalonia stylt darüber ohnehin einheitlicher).
    /// Nicht-modal (Show(), nicht ShowDialog()) — läuft parallel zum Hauptfenster weiter, exakt wie
    /// unter Windows.</summary>
    public sealed class DownloadProgressDialog : Window
    {
        private static readonly IBrush BrushGreen  = new SolidColorBrush(Color.Parse("#2ECC71"));
        private static readonly IBrush BrushAmber  = new SolidColorBrush(Color.Parse("#F39C12"));
        private static readonly IBrush BrushBlue   = new SolidColorBrush(Color.Parse("#3AAEEF"));
        private static readonly IBrush BrushDim    = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#336B9E"));

        public event Action? CancelRequested;
        public event Action<string>? FasterMirrorRequested;

        private readonly StackPanel _itemsPanel;
        private readonly TextBlock _summaryText;
        private readonly StackPanel _overallPanel;
        private readonly ProgressBar _overallBar;
        private readonly TextBlock _overallText;
        private readonly int _total;
        private readonly bool _hasDownload;
        private readonly bool _hasCopy;

        private sealed class Row
        {
            public required Border Container;
            public required TextBlock NameText;
            public required ProgressBar Bar;
            public required TextBlock PercentText;
            public required TextBlock StatusText;
            public required Button FasterBtn;
            public required string OriginalName;
        }

        private sealed class ItemState
        {
            public Row? UiRow;
            public double DlFrac;
            public double CopyFrac;
            public bool Done;
        }

        private readonly Dictionary<string, ItemState> _items = new(StringComparer.OrdinalIgnoreCase);

        public DownloadProgressDialog(IEnumerable<string> isoNames, bool hasDownload = true, bool hasCopy = false)
        {
            var names = isoNames.ToList();
            _total = names.Count; _hasDownload = hasDownload; _hasCopy = hasCopy;
            Title = LocalizationService.T(Str.Dl_ProgressDialog_Title);
            Width = 540;
            double maxH = Screens.Primary?.WorkingArea.Height ?? 720;
            MinHeight = Math.Min(320, maxH - 60);
            MaxHeight = Math.Max(320, maxH - 60);
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };

            var header = new StackPanel();
            _summaryText = new TextBlock
            {
                Text = LocalizationService.T(names.Count == 1 ? Str.Dl_DownloadRunningSingle : Str.Dl_DownloadRunningMultiple),
                FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10), Foreground = BrushHeader,
            };
            header.Children.Add(_summaryText);

            _overallPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12), IsVisible = _total > 1 };
            _overallText = new TextBlock { Text = string.Format(LocalizationService.T(Str.Dl_OverallStatus), 0, 0, _total), FontSize = 10.5, Foreground = BrushDim, Margin = new Thickness(0, 0, 0, 3) };
            _overallBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8 };
            _overallPanel.Children.Add(_overallText);
            _overallPanel.Children.Add(_overallBar);
            header.Children.Add(_overallPanel);
            Grid.SetRow(header, 0); root.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) };
            _itemsPanel = new StackPanel();
            scroll.Content = _itemsPanel;
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);

            foreach (string name in names) GetOrCreate(name);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var bCancel = new Button { Content = LocalizationService.T(Str.Dl_Btn_CancelX), Width = 120, Classes = { "danger" }, Margin = new Thickness(0, 0, 8, 0) };
            bCancel.Click += (_, _) => CancelRequested?.Invoke();
            var bClose = new Button { Content = LocalizationService.T(Str.Db_Btn_CloseSimple), Width = 110, Classes = { "ghost" } };
            bClose.Click += (_, _) => Close();
            btnRow.Children.Add(bCancel);
            btnRow.Children.Add(bClose);
            Grid.SetRow(btnRow, 2); root.Children.Add(btnRow);

            Content = root;
        }

        private Row AddRow(string name)
        {
            var border = new Border { BorderBrush = BrushBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 8) };
            var stack = new StackPanel();

            var hRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            var nameText = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = BrushHeader };
            Grid.SetColumn(nameText, 0); hRow.Children.Add(nameText);

            var fasterBtn = new Button
            {
                Content = LocalizationService.T(Str.Dl_Btn_Faster), FontSize = 9, Padding = new Thickness(8, 1, 8, 1), Height = 20,
                Classes = { "ghost" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            };
            ToolTip.SetTip(fasterBtn, LocalizationService.T(Str.Dl_Tooltip_Faster));
            fasterBtn.Click += (_, _) => FasterMirrorRequested?.Invoke(name);
            Grid.SetColumn(fasterBtn, 1); hRow.Children.Add(fasterBtn);

            var pctText = new TextBlock { Text = "0%", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), Foreground = BrushBlue, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pctText, 2); hRow.Children.Add(pctText);
            stack.Children.Add(hRow);

            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8, Margin = new Thickness(0, 5, 0, 3) };
            stack.Children.Add(bar);

            var stat = new TextBlock { Text = LocalizationService.T(Str.Dl_Waiting), FontSize = 10.5, Foreground = BrushDim, TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(stat);

            border.Child = stack;
            _itemsPanel.Children.Add(border);
            return new Row { Container = border, NameText = nameText, Bar = bar, PercentText = pctText, StatusText = stat, FasterBtn = fasterBtn, OriginalName = name };
        }

        private ItemState GetOrCreate(string name)
        {
            if (_items.TryGetValue(name, out var s)) return s;
            s = new ItemState { UiRow = AddRow(name) };
            _items[name] = s;
            return s;
        }

        private static IBrush ProgressColor(int percent) =>
            percent >= 90 ? BrushGreen : percent < 30 ? BrushAmber : BrushBlue;

        /// <summary>Windows-Pendant: UpdateDownload. Im reinen Download-Modus gilt ein Eintrag bei
        /// 100% als fertig und wird aus der Liste entfernt.</summary>
        public void UpdateDownload(string name, int percent, string status, bool canFasterMirror = false)
        {
            var it = GetOrCreate(name); if (it.Done) return;
            int c = Math.Clamp(percent, 0, 100);
            it.DlFrac = c / 100.0;
            if (it.UiRow is { } r)
            {
                r.Bar.Value = c; r.Bar.Foreground = ProgressColor(c); r.PercentText.Text = $"{c}%"; r.StatusText.Text = status;
                r.FasterBtn.IsVisible = canFasterMirror;
            }
            if (!_hasCopy && c >= 100) { MarkDone(name); return; }
            RecomputeOverall();
        }

        /// <summary>Windows-Pendant: UpdateCopy. 100% == erfolgreich kopiert (Fehlschläge melden die
        /// Worker mit 0%, nicht 100%) → Eintrag entfernen.</summary>
        public void UpdateCopy(string name, int percent, string status)
        {
            var it = GetOrCreate(name); if (it.Done) return;
            int c = Math.Clamp(percent, 0, 100);
            it.CopyFrac = c / 100.0;
            if (it.UiRow is { } r)
            {
                r.NameText.Text = string.Format(LocalizationService.T(Str.Dl_CopyingToStickSuffix), r.OriginalName);
                r.Bar.Value = c; r.Bar.Foreground = ProgressColor(c); r.PercentText.Text = $"{c}%"; r.StatusText.Text = status;
                r.FasterBtn.IsVisible = false;
            }
            if (c >= 100) { MarkDone(name); return; }
            RecomputeOverall();
        }

        private void MarkDone(string name)
        {
            var it = GetOrCreate(name);
            if (it.Done) return;
            it.Done = true; it.DlFrac = 1; it.CopyFrac = 1;
            if (it.UiRow is { } r) { _itemsPanel.Children.Remove(r.Container); it.UiRow = null; }
            RecomputeOverall();
        }

        private void RecomputeOverall()
        {
            if (_total <= 0) return;
            double sum = 0; int done = 0;
            foreach (var it in _items.Values)
            {
                if (it.Done) { sum += 1.0; done++; }
                else if (_hasDownload && _hasCopy) sum += 0.5 * it.DlFrac + 0.5 * it.CopyFrac;
                else if (_hasCopy) sum += it.CopyFrac;
                else sum += it.DlFrac;
            }
            int pct = Math.Clamp((int)Math.Round(sum * 100.0 / _total), 0, 100);
            _overallBar.Value = pct;
            _overallBar.Foreground = ProgressColor(pct);
            _overallText.Text = string.Format(LocalizationService.T(Str.Dl_OverallStatus), pct, done, _total);
        }

        /// <summary>Windows-Pendant: SetOverallComplete — zeigt die Batch-Zusammenfassung in der
        /// Kopfzeile, zählt dabei die TATSÄCHLICH fertigen Einträge (nicht blind 100%), damit ein
        /// teilweiser Fehlschlag sichtbar bleibt.</summary>
        public void SetOverallComplete(string summary)
        {
            RecomputeOverall();
            bool allDone = _items.Values.All(it => it.Done);
            _summaryText.Text = string.Format(LocalizationService.T(allDone ? Str.Log_OperationSucceededLogPrefix : Str.Dl_SummaryPartial), summary);
        }
    }
}
