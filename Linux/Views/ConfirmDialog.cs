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

        private ConfirmDialog(string title, string message, bool withCancel) : this(title, message)
        {
            if (!withCancel) return;
            // Dritter Button für den Windows-YesNoCancel-Fall (z.B. "Nach dem Download auf den
            // Stick kopieren?" — Abbrechen bricht den gesamten Download ab, nicht nur die Frage).
            var btns = (StackPanel)((StackPanel)Content!).Children[1];
            var cancel = new Button { Content = LocalizationService.T(Str.Db_Btn_Cancel), Classes = { "ghost" }, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (_, _) => Close((bool?)null);
            btns.Children.Add(cancel);
        }

        /// <summary>Windows-Pendant: MessageBox.Show(..., MessageBoxButton.YesNoCancel, ...).
        /// true = Ja, false = Nein, null = Abbrechen.</summary>
        public static async Task<bool?> ShowYesNoCancelAsync(Window owner, string title, string message) =>
            await new ConfirmDialog(title, message, withCancel: true).ShowDialog<bool?>(owner);
    }
}
