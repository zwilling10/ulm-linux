using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Workers;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' DbHealthCheckDialog (WPF) —
    /// reines Code-behind wie das Original. Grün/Rot sind hier als feste Hex-Werte hinterlegt
    /// (identisch zu ColGreen/ColRed in Linux/Themes/DarkTheme.axaml) statt per Resource-Lookup,
    /// weil das für zwei Sonderfarben in einem einzelnen Dialog robuster/einfacher ist als
    /// Avalonias TryFindResource-API aus purem Code-behind heraus.</summary>
    public sealed class DbHealthCheckDialog : Window
    {
        private static readonly IBrush BrushGreen = new SolidColorBrush(Color.Parse("#2ECC71"));
        private static readonly IBrush BrushRed   = new SolidColorBrush(Color.Parse("#E74C3C"));
        private static readonly IBrush BrushDim   = new SolidColorBrush(Color.Parse("#8BA3BE"));

        public DbHealthCheckDialog(IReadOnlyList<VersionCheckEntryResult> results)
        {
            int failed = results.Count(r => !r.Resolved);
            Title = LocalizationService.T(Str.Db_HealthCheckDialog_Title);
            Width = 600;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };

            var header = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = failed == 0 ? BrushGreen : BrushRed,
                Text = failed == 0
                    ? string.Format(LocalizationService.T(Str.Db_HealthCheck_AllReachable), results.Count)
                    : string.Format(LocalizationService.T(Str.Db_HealthCheck_SomeUnreachable), failed, results.Count),
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var list = new StackPanel();
            foreach (VersionCheckEntryResult r in results.OrderBy(r => r.Resolved).ThenBy(r => r.Name))
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
                row.Children.Add(new TextBlock { Text = r.Resolved ? "✅" : "❌", FontSize = 12 });

                var nameTb = new TextBlock
                {
                    Text = r.Name, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(nameTb, 1);
                row.Children.Add(nameTb);

                var infoTb = new TextBlock
                {
                    Text = r.Resolved ? $"v{r.RemoteVersion}" : LocalizationService.T(Str.Log_Unreachable),
                    FontSize = 10.5, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = r.Resolved ? BrushDim : BrushRed,
                };
                Grid.SetColumn(infoTb, 2);
                row.Children.Add(infoTb);

                list.Children.Add(row);
            }

            var scroll = new ScrollViewer { Content = list };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            if (failed > 0)
            {
                var tip = new TextBlock
                {
                    FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
                    Foreground = BrushDim, Text = LocalizationService.T(Str.Db_HealthCheck_Tip),
                };
                Grid.SetRow(tip, 2);
                root.Children.Add(tip);
            }

            var close = new Button
            {
                Content = LocalizationService.T(Str.Db_Btn_Close), MinWidth = 130, Classes = { "primary" },
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0),
            };
            close.Click += (_, _) => Close();
            Grid.SetRow(close, 3);
            root.Children.Add(close);

            Content = root;
        }
    }
}
