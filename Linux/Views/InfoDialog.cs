using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Minimaler Nur-OK-Hinweisdialog — Avalonia-Pendant zu Windows'
    /// `MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information)`. Analog
    /// zum bestehenden ConfirmDialog (Ja/Nein), nur mit einem einzigen Button.
    /// Optionales <paramref name="image"/> (neu, Nutzerwunsch 2026-09-08 für die ISO-Suche-
    /// Vorschau: "mit einem Vorschaubild der Distro") — bestehende Aufrufer ohne Bild bleiben
    /// unverändert (Default null).</summary>
    public sealed class InfoDialog : Window
    {
        private InfoDialog(string title, string message, Bitmap? image)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(20) };
            if (image is not null)
            {
                root.Children.Add(new Image
                {
                    Source = image, Stretch = Stretch.Uniform, MaxHeight = 220,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14),
                });
            }
            root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            var ok = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => Close();
            root.Children.Add(ok);
            Content = root;
        }

        public static async Task ShowAsync(Window owner, string title, string message, Bitmap? image = null) =>
            await new InfoDialog(title, message, image).ShowDialog(owner);
    }
}
