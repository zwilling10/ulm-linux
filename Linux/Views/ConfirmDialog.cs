using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Minimaler Ja/Nein-Bestätigungsdialog — Avalonia hat kein WPF-`MessageBox`-Äquivalent.
    /// Ersetzt die WPF-`MessageBox.Show(..., MessageBoxButton.YesNo, ...)`-Aufrufe der
    /// Datenbank-Dialoge. Nutzt die bereits vorhandenen, generischen Str.Row_Yes/Row_No-Werte
    /// ("Ja"/"Nein") statt (wie unter Windows) an der System-Sprache hängender nativer
    /// MessageBox-Buttons — auf Linux gibt es diese System-Buttons ohnehin nicht.</summary>
    public sealed class ConfirmDialog : Window
    {
        private ConfirmDialog(string title, string message)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var yes = new Button { Content = LocalizationService.T(Str.Row_Yes), Classes = { "danger" }, MinWidth = 90 };
            yes.Click += (_, _) => Close(true);
            var no = new Button { Content = LocalizationService.T(Str.Row_No), Classes = { "ghost" }, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            no.Click += (_, _) => Close(false);
            btns.Children.Add(yes);
            btns.Children.Add(no);
            root.Children.Add(btns);
            Content = root;
        }

        public static async Task<bool> ShowAsync(Window owner, string title, string message) =>
            await new ConfirmDialog(title, message).ShowDialog<bool>(owner);
    }
}
