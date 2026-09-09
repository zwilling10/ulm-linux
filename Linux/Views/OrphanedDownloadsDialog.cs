using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Windows' OrphanedDownloadsDialog (Views/Dialogs/
    /// DownloadDialogs.cs) — Teil des "Datenmüll-Schutz"-Features (Nutzerwunsch 2026-09-04):
    /// zeigt verwaiste/unvollständige/leere ISO-Dateien im Arbeitsverzeichnis, alle vorausgewählt,
    /// zum gezielten Löschen.</summary>
    public sealed class OrphanedDownloadsDialog : Window
    {
        public List<string> ToDelete { get; } = new();
        private readonly Dictionary<string, CheckBox> _checks = new();

        public OrphanedDownloadsDialog(List<(string Path, long Size)> files, string? title = null, string? itemLabel = null)
        {
            Title = title ?? LocalizationService.T(Str.Dl_OrphanedDefaultTitle);
            string resolvedItemLabel = itemLabel ?? LocalizationService.T(Str.Dl_OrphanedDefaultItemLabel);
            Width = 560; Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };

            root.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationService.T(Str.Dl_OrphanedFoundText), files.Count, resolvedItemLabel),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12.5,
            });

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list = new StackPanel();
            foreach (var (path, size) in files)
            {
                var chk = new CheckBox { Content = $"{Path.GetFileName(path)}   ({FormatBytes(size)})", IsChecked = true, Margin = new Thickness(0, 4, 0, 4), FontSize = 11.5 };
                _checks[path] = chk;
                list.Children.Add(chk);
            }
            scroll.Content = list;
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var bSkip = new Button { Content = LocalizationService.T(Str.Db_Btn_Skip), Width = 120, Classes = { "ghost" }, Margin = new Thickness(0, 0, 8, 0) };
            bSkip.Click += (_, _) => Close(false);
            var bDel = new Button { Content = LocalizationService.T(Str.Dl_Btn_DeleteSelected), Width = 190, Classes = { "danger" } };
            bDel.Click += (_, _) =>
            {
                ToDelete.Clear();
                foreach (var kvp in _checks) if (kvp.Value.IsChecked == true) ToDelete.Add(kvp.Key);
                Close(true);
            };
            btnRow.Children.Add(bSkip);
            btnRow.Children.Add(bDel);
            Grid.SetRow(btnRow, 2); root.Children.Add(btnRow);

            Content = root;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return i == 0 ? $"{(long)v} B" : $"{v:F1} {units[i]}";
        }
    }
}
