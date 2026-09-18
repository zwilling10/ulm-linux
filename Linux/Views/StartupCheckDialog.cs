using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;

namespace ULM.Linux.Views
{
    /// <summary>Modales "Bitte warten"-Fenster mit Fortschrittsbalken, das den Start-
    /// Versionscheck (LinuxMainViewModel.TriggerAutoVersionCheckAsync — löst pro Katalog-Eintrag
    /// die aktuelle Version/URL auf) begleitet. Nutzerwunsch: der Anwender soll die App nicht
    /// benutzen können, bevor dieser Check durch ist — vorher gab es dafür nur einen leicht zu
    /// übersehenden pulsierenden Text in der Kopfzeile (ScanHintTb), kein blockierendes Fenster.
    /// Schließt sich automatisch über AutoVersionCheckCompleted (siehe App.axaml.cs).</summary>
    public sealed class StartupCheckDialog : Window
    {
        private readonly RotateTransform _spinnerRotation = new(0);
        private readonly DispatcherTimer _timer;

        /// <summary>Standard-Aufruf: blockierender Start-Versionscheck (App.axaml.cs).</summary>
        public StartupCheckDialog(LinuxMainViewModel vm)
            : this(vm, LocalizationService.T(Str.Msg_PleaseWait),
                   nameof(LinuxMainViewModel.StartupHintText), nameof(LinuxMainViewModel.StartupHintPercent))
        {
        }

        /// <summary>Nutzerwunsch (2026-09-17): dasselbe Fenster (gleiche Optik/Spinner) auch
        /// nicht-blockierend für den Stick-Scan wiederverwenden, mit eigenem Text ("Bitte Geduld")
        /// und eigenen gebundenen Fortschritts-Properties (UsbScanHintFullText-Äquivalent), statt
        /// eine zweite, fast identische Dialogklasse zu bauen.</summary>
        public StartupCheckDialog(LinuxMainViewModel vm, string headerText, string statusPropertyName, string percentPropertyName)
        {
            Title = headerText;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Kein System-Schließen-Button-Handling nötig — der Anwender hat ohnehin nichts
            // zu tun außer zu warten; ein Klick auf X würde nur dieses Fenster schließen, die
            // App bliebe dahinter normal nutzbar (kein Datenverlust-Risiko).

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
                VerticalAlignment = VerticalAlignment.Center,
            };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };
            text.Children.Add(new TextBlock
            {
                Text = headerText,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
            });

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            status.Bind(TextBlock.TextProperty, new Binding(statusPropertyName) { Source = vm });
            text.Children.Add(status);

            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18 };
            bar.Bind(ProgressBar.ValueProperty, new Binding(percentPropertyName) { Source = vm });
            text.Children.Add(bar);

            var root = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("56,*") };
            root.Children.Add(spinner);
            Grid.SetColumn(text, 1);
            root.Children.Add(text);
            Content = root;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            _timer.Tick += (_, _) => _spinnerRotation.Angle = (_spinnerRotation.Angle + 12) % 360;
            Opened += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }
    }
}
