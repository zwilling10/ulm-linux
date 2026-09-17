using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
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
        public StartupCheckDialog(LinuxMainViewModel vm)
        {
            Title = LocalizationService.T(Str.Msg_PleaseWait);
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Kein System-Schließen-Button-Handling nötig — der Anwender hat ohnehin nichts
            // zu tun außer zu warten; ein Klick auf X würde nur dieses Fenster schließen, die
            // App bliebe dahinter normal nutzbar (kein Datenverlust-Risiko).

            var root = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };

            root.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(Str.Msg_PleaseWait),
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var status = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            status.Bind(TextBlock.TextProperty, new Binding(nameof(LinuxMainViewModel.StartupHintText)) { Source = vm });
            root.Children.Add(status);

            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18 };
            bar.Bind(ProgressBar.ValueProperty, new Binding(nameof(LinuxMainViewModel.StartupHintPercent)) { Source = vm });
            root.Children.Add(bar);

            Content = root;
        }
    }
}
