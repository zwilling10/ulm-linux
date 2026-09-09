using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    public sealed class StartupOnlineCheckDialog : Window
    {
        public const string WaitMessage = "Bitte warten, ULM prüft die Verfügbarkeit/Erreichbarkeit  und Version`s Aktualisierung der Distros automatisch";

        private readonly RotateTransform _spinnerRotation = new(0);
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _status;
        private readonly ProgressBar _progress;
        private readonly TextBlock _percent;

        public StartupOnlineCheckDialog()
        {
            Title = Constants.AppTitle;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var spinner = new TextBlock
            {
                Text = "⟳",
                FontSize = 32,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.DeepSkyBlue,
                RenderTransform = _spinnerRotation,
                RenderTransformOrigin = RelativePoint.Center,
                Width = 48,
                Height = 48,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _status = new TextBlock
            {
                Text = "Online-Scan wird vorbereitet ...",
                Foreground = Brushes.LightSteelBlue,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            };
            _progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 14, Margin = new Thickness(0, 12, 0, 0) };
            _percent = new TextBlock { Text = "0%", Foreground = Brushes.LightSteelBlue, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = WaitMessage,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(_status);
            text.Children.Add(_progress);
            text.Children.Add(_percent);

            var root = new Grid { Margin = new Thickness(20), ColumnDefinitions = new ColumnDefinitions("56,*") };
            root.Children.Add(spinner);
            Grid.SetColumn(text, 1);
            root.Children.Add(text);
            Content = root;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            _timer.Tick += (_, _) => _spinnerRotation.Angle = (_spinnerRotation.Angle + 12) % 360;
            Opened += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        public void SetStatus(string status) => _status.Text = status;
        public void SetProgress(int percent)
        {
            int p = Math.Clamp(percent, 0, 100);
            _progress.Value = p;
            _percent.Text = $"{p}%";
        }
    }
}
