using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Windows' DownloadSlotsDialog (Views/Dialogs/DownloadDialogs.cs)
    /// — Nutzerwunsch (2026-09-04): "genauso wie Windows", nicht die zuvor gebaute Inline-
    /// NumericUpDown-Lösung. Misst die Verbindungsgeschwindigkeit über den bereits plattformneutral
    /// verlinkten HttpService.MeasureDownloadSpeedMbpsAsync (Core/Services/HttpService.cs) und
    /// empfiehlt daraus eine Slot-Zahl — RecommendSlots ist absichtlich als reine, private Kopie der
    /// Windows-Tabelle gehalten statt in Core ausgelagert (winziger, stabiler Codeblock; eine
    /// Core-Extraktion hätte das getestete Windows-DownloadDialogs.cs anfassen müssen, ohne
    /// erkennbaren Zusatznutzen für dieses Maß an Duplikation).</summary>
    public sealed class DownloadSlotsDialog : Window
    {
        public int ChosenSlots { get; private set; } = 1;

        private readonly int _maxSlots;
        private int _recommended = 1;
        private bool _testDone;
        private readonly TextBlock _statusText;
        private readonly TextBlock _resultText;
        private readonly Slider _slider;
        private readonly TextBlock _sliderValueText;
        private readonly CheckBox _chkAuto;
        private readonly Button _btnOk;

        public DownloadSlotsDialog(int queueCount, int maxSlots)
        {
            _maxSlots = Math.Max(1, Math.Min(maxSlots, queueCount));
            Title = LocalizationService.T(Str.Dl_SlotsDialog_Title);
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(22) };

            root.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationService.T(Str.Dl_SlotsSelectedCount), queueCount),
                FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 14,
                Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap,
            });

            _statusText = new TextBlock { Text = LocalizationService.T(Str.Dl_TestingSpeed), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_statusText);

            _resultText = new TextBlock { Text = string.Empty, FontSize = 12, IsVisible = false, Margin = new Thickness(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_resultText);

            root.Children.Add(new TextBlock { Text = LocalizationService.T(Str.Dl_Label_ParallelDownloads), FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

            var slRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            _slider = new Slider { Minimum = 1, Maximum = _maxSlots, Value = 1, TickFrequency = 1, IsSnapToTickEnabled = true, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            _sliderValueText = new TextBlock { Text = "1", FontWeight = Avalonia.Media.FontWeight.Bold, Width = 28, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) _sliderValueText.Text = ((int)_slider.Value).ToString(); };
            Grid.SetColumn(_slider, 0); slRow.Children.Add(_slider);
            Grid.SetColumn(_sliderValueText, 1); slRow.Children.Add(_sliderValueText);
            root.Children.Add(slRow);

            _chkAuto = new CheckBox { Content = LocalizationService.T(Str.Dl_Chk_UseRecommendation), IsChecked = true, Margin = new Thickness(0, 14, 0, 0), FontSize = 12 };
            _chkAuto.IsCheckedChanged += (_, _) => ApplyAutoState(_chkAuto.IsChecked == true);
            root.Children.Add(_chkAuto);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 28, 0, 0) };
            var bCancel = new Button { Content = LocalizationService.T(Str.Dl_Btn_Cancel), Width = 100, Classes = { "ghost" }, Margin = new Thickness(0, 0, 8, 0) };
            bCancel.Click += (_, _) => Close(false);
            _btnOk = new Button { Content = LocalizationService.T(Str.Dl_Btn_StartDownloads), Width = 175, Classes = { "primary" }, IsEnabled = false };
            _btnOk.Click += (_, _) => { ChosenSlots = (int)_slider.Value; Close(true); };
            btnRow.Children.Add(bCancel);
            btnRow.Children.Add(_btnOk);
            root.Children.Add(btnRow);

            Content = root;
            Loaded += async (_, _) => await RunSpeedTestAsync();
        }

        private void ApplyAutoState(bool auto)
        {
            _slider.IsEnabled = !auto && _testDone;
            if (auto) _slider.Value = _recommended;
        }

        private async Task RunSpeedTestAsync()
        {
            double mbps = await HttpService.Instance.MeasureDownloadSpeedMbpsAsync(CancellationToken.None).ConfigureAwait(true);
            _recommended = RecommendSlots(mbps, _maxSlots);
            _testDone = true;
            _statusText.IsVisible = false;
            _resultText.IsVisible = true;
            _resultText.Text = mbps > 0
                ? string.Format(LocalizationService.T(Str.Dl_SpeedTestResult), mbps.ToString("F1"), _recommended)
                : string.Format(LocalizationService.T(Str.Dl_SpeedTestFailed), _recommended);
            _slider.Value = _recommended;
            _slider.IsEnabled = _chkAuto.IsChecked != true;
            _btnOk.IsEnabled = true;
        }

        private static int RecommendSlots(double mbps, int max) => Math.Max(1, Math.Min(max, mbps switch
        { <= 0 => 2, < 8 => 1, < 25 => 2, < 60 => 3, < 120 => 4, < 250 => 5, _ => 6 }));

        /// <summary>Zeigt den Dialog; null == Abbrechen, sonst die gewählte Slot-Zahl.</summary>
        public static async Task<int?> ShowAsync(Window owner, int queueCount, int maxSlots)
        {
            var dlg = new DownloadSlotsDialog(queueCount, maxSlots);
            bool started = await dlg.ShowDialog<bool>(owner);
            return started ? dlg.ChosenSlots : null;
        }
    }
}
