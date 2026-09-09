using System.Collections.Generic;
using System.Linq;
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
    public enum NewerVersionChoice { Replace, Add, Skip }

    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' NewerVersionOnStickDialog
    /// (WPF) — reines Code-behind wie das Original. Zeigt für jede DB-Distro, deren Stick-Datei
    /// eine neuere Version trägt als die Datenbank kennt, eine Version-Gegenüberstellung +
    /// Replace/Add/Skip-Auswahl.</summary>
    public sealed class NewerVersionOnStickDialog : Window
    {
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushMid    = new SolidColorBrush(Color.Parse("#B8C9DC"));
        private static readonly IBrush BrushDim    = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushBlue   = new SolidColorBrush(Color.Parse("#3AAEEF"));
        private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#336B9E"));
        private static readonly IBrush BrushCard   = new SolidColorBrush(Color.Parse("#132A44"));

        public List<(IsoEntry DbEntry, UsbService.StickIso StickIso, NewerVersionChoice Choice)> Results { get; } = new();

        private sealed class RowData
        {
            public required IsoEntry DbEntry = null!;
            public required UsbService.StickIso StickIso = null!;
            public required RadioButton RbReplace = null!;
            public required RadioButton RbAdd = null!;
            public required RadioButton RbSkip = null!;
        }

        private readonly List<RowData> _rows = new();

        public NewerVersionOnStickDialog(IReadOnlyList<(IsoEntry DbEntry, UsbService.StickIso StickIso)> matches)
        {
            Title = LocalizationService.T(Str.Db_NewerVersionDialog_Title);
            Width = 700; Height = 560;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12.5,
                Foreground = BrushHeader, Text = string.Format(LocalizationService.T(Str.Db_NewerVersionDialog_Info), matches.Count),
            });

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel();

            for (int i = 0; i < matches.Count; i++)
            {
                var (dbEntry, stickIso) = matches[i];
                string grp = $"nvsg_{i}";
                string dbVer = HttpService.ExtractVersion(dbEntry.Filename);
                string stickVer = HttpService.ExtractVersion(stickIso.Filename);

                var block = new Border
                {
                    BorderBrush = BrushBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 12), Background = BrushCard,
                };
                var inner = new StackPanel();
                inner.Children.Add(new TextBlock
                {
                    Text = string.Format(LocalizationService.T(Str.Db_CategoryNameHeader), dbEntry.Category, dbEntry.Name),
                    FontWeight = FontWeight.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 10), Foreground = BrushHeader,
                });

                var vg = new Grid { Margin = new Thickness(0, 0, 0, 12), ColumnDefinitions = new ColumnDefinitions("105,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
                void AddVRow(int row, string lbl, string fn, string ver, bool newer)
                {
                    var l = new TextBlock { Text = lbl, FontSize = 11, Foreground = BrushDim, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(l, row); Grid.SetColumn(l, 0); vg.Children.Add(l);
                    var v = new TextBlock
                    {
                        Text = string.Format(LocalizationService.T(Str.Db_FilenameVersionLine), fn, ver), FontSize = 11,
                        FontWeight = newer ? FontWeight.SemiBold : FontWeight.Normal,
                        Foreground = newer ? BrushBlue : BrushMid,
                        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                    };
                    ToolTip.SetTip(v, fn);
                    Grid.SetRow(v, row); Grid.SetColumn(v, 1); vg.Children.Add(v);
                }
                AddVRow(0, LocalizationService.T(Str.Db_Label_Database), dbEntry.Filename, dbVer, false);
                AddVRow(1, LocalizationService.T(Str.Db_Label_OnStick), stickIso.Filename, stickVer, true);
                inner.Children.Add(vg);

                var rbReplace = MakeRadio(LocalizationService.T(Str.Db_Radio_Replace), grp, true);
                var rbAdd = MakeRadio(LocalizationService.T(Str.Db_Radio_Add), grp, false);
                var rbSkip = MakeRadio(LocalizationService.T(Str.Db_Radio_Skip), grp, false);
                inner.Children.Add(rbReplace);
                inner.Children.Add(rbAdd);
                inner.Children.Add(rbSkip);
                block.Child = inner;
                panel.Children.Add(block);
                _rows.Add(new RowData { DbEntry = dbEntry, StickIso = stickIso, RbReplace = rbReplace, RbAdd = rbAdd, RbSkip = rbSkip });
            }

            scroll.Content = panel;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var br = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var bAllR = new Button { Content = LocalizationService.T(Str.Db_Btn_ReplaceAll), Classes = { "ghost" }, Width = 120, Margin = new Thickness(0, 0, 6, 0) };
            ToolTip.SetTip(bAllR, LocalizationService.T(Str.Db_Tooltip_ReplaceAll));
            bAllR.Click += (_, _) => { foreach (var r in _rows) { r.RbReplace.IsChecked = true; r.RbAdd.IsChecked = false; r.RbSkip.IsChecked = false; } };
            var bAllS = new Button { Content = LocalizationService.T(Str.Db_Btn_SkipAll), Classes = { "ghost" }, Width = 140, Margin = new Thickness(0, 0, 14, 0) };
            bAllS.Click += (_, _) => { foreach (var r in _rows) { r.RbSkip.IsChecked = true; r.RbReplace.IsChecked = false; r.RbAdd.IsChecked = false; } };
            var bCancel = new Button { Content = LocalizationService.T(Str.Db_Btn_Cancel), Classes = { "ghost" }, Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            bCancel.Click += (_, _) => Close(false);
            var bOk = new Button { Content = LocalizationService.T(Str.Db_Btn_ApplySelection), Classes = { "primary" }, Width = 160 };
            bOk.Click += BtnOk_Click;
            br.Children.Add(bAllR); br.Children.Add(bAllS); br.Children.Add(bCancel); br.Children.Add(bOk);
            Grid.SetRow(br, 2);
            root.Children.Add(br);
            Content = root;
        }

        private static RadioButton MakeRadio(string text, string groupName, bool isChecked) => new()
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = BrushHeader },
            GroupName = groupName, IsChecked = isChecked, Margin = new Thickness(0, 3, 0, 3),
        };

        private void BtnOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            foreach (var row in _rows)
            {
                NewerVersionChoice choice =
                    row.RbReplace.IsChecked == true ? NewerVersionChoice.Replace :
                    row.RbAdd.IsChecked == true ? NewerVersionChoice.Add : NewerVersionChoice.Skip;
                Results.Add((row.DbEntry, row.StickIso, choice));
            }
            Close(Results.Any(r => r.Choice != NewerVersionChoice.Skip));
        }
    }
}
