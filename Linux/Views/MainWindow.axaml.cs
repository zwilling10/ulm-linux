using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ULM.Linux.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
