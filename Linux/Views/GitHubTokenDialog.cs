using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' GitHubTokenDialog (WPF) —
    /// bewusst reines Code-behind wie das Original, kein .axaml (Struktur ist trivial: ein Feld,
    /// zwei Buttons). Styling kommt automatisch aus Linux/Themes/DarkTheme.axaml (Window/TextBox/
    /// Button/TextBlock sind dort per Typ-Selector gestylt) — hier nur die primary/ghost-Klassen
    /// für die Buttons.</summary>
    public sealed class GitHubTokenDialog : Window
    {
        private readonly TextBox _tokenBox;

        public GitHubTokenDialog(string currentToken)
        {
            Title = LocalizationService.T(Str.Db_GitHubTokenDialog_Title);
            Width = 480;
            Height = 240;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(Str.Db_GitHubTokenDialog_Description),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 14),
            });

            _tokenBox = new TextBox
            {
                Text = currentToken,
                Margin = new Thickness(0, 0, 0, 16),
                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                Watermark = "ghp_…",
            };
            root.Children.Add(_tokenBox);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = LocalizationService.T(Str.Db_Btn_Save), Classes = { "primary" }, MinWidth = 110 };
            ok.Click += (_, _) => Close(_tokenBox.Text?.Trim() ?? string.Empty);
            var cancel = new Button
            {
                Content = LocalizationService.T(Str.Db_Btn_Cancel), Classes = { "ghost" },
                MinWidth = 100, Margin = new Thickness(8, 0, 0, 0),
            };
            cancel.Click += (_, _) => Close(null);
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            Content = root;
        }
    }
}
