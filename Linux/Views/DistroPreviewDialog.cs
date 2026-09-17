using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    public sealed class DistroPreviewDialog : Window
    {
        private static readonly IBrush BrushBg = new SolidColorBrush(Color.Parse("#0B1220"));
        private static readonly IBrush BrushCard = new SolidColorBrush(Color.Parse("#112036"));
        private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#284767"));
        private static readonly IBrush BrushDim = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushLBlue = new SolidColorBrush(Color.Parse("#16324F"));
        private static readonly IBrush BrushRed = new SolidColorBrush(Color.Parse("#F7768E"));

        private readonly string _name;
        private readonly string _slug;
        private readonly IReadOnlyList<string> _tags;
        private readonly StackPanel _contentPanel;

        public DistroPreviewDialog(string name, string slug, IReadOnlyList<string> tags)
        {
            _name = name;
            _slug = slug;
            _tags = tags;

            Title = string.Format(LocalizationService.T(Str.Preview_DialogTitle), name);
            Width = 380;
            Height = 520;
            MinWidth = 300;
            MinHeight = 320;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = BrushBg;

            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,Auto") };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _contentPanel = new StackPanel();
            _contentPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(Str.Db_Loading),
                Foreground = BrushDim,
                FontSize = 12,
                Margin = new Thickness(0, 24, 0, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            scroll.Content = _contentPanel;
            Grid.SetRow(scroll, 0);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var closeBtn = new Button { Content = LocalizationService.T(Str.Db_Btn_CloseSimple), Classes = { "ghost" }, MinWidth = 110 };
            closeBtn.Click += (_, _) => Close();
            DockPanel.SetDock(closeBtn, Dock.Right);

            var openBtn = new Button { Content = LocalizationService.T(Str.Preview_OpenInBrowser), Classes = { "ghost" } };
            openBtn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo($"https://distrowatch.com/{_slug}") { UseShellExecute = true }); }
                catch (Exception ex) { Debug.WriteLine($"[PreviewOpen] {ex.Message}"); }
            };

            footer.Children.Add(closeBtn);
            footer.Children.Add(openBtn);
            Grid.SetRow(footer, 1);

            root.Children.Add(scroll);
            root.Children.Add(footer);
            Content = root;

            Opened += (_, _) => PositionNextToOwner();
            Opened += async (_, _) => await LoadAsync();
        }

        private void PositionNextToOwner()
        {
            if (Owner is not Window owner) return;

            PixelRect workArea = Screens.ScreenFromWindow(owner)?.WorkingArea
                                 ?? Screens.Primary?.WorkingArea
                                 ?? new PixelRect(0, 0, 1920, 1080);
            double ownerWidth = owner.Bounds.Width > 0 ? owner.Bounds.Width : owner.Width;
            Position = ComputeDockedPosition(owner.Position, ownerWidth, Width, workArea);
        }

        internal static PixelPoint ComputeDockedPosition(PixelPoint ownerPosition, double ownerWidth, double previewWidth, PixelRect workArea)
        {
            const int gap = 8;
            int width = (int)Math.Ceiling(previewWidth);
            int left = ownerPosition.X + (int)Math.Ceiling(ownerWidth) + gap;

            if (left + width > workArea.Right)
            {
                int leftFallback = ownerPosition.X - width - gap;
                if (leftFallback >= workArea.X) left = leftFallback;
            }

            return new PixelPoint(left, ownerPosition.Y);
        }

        private async Task LoadAsync()
        {
            var preview = await DistroPreviewService.Instance.GetPreviewAsync(_name, _slug).ConfigureAwait(true);
            _contentPanel.Children.Clear();

            if (preview is null)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = string.Format(LocalizationService.T(Str.Preview_LoadError), _slug),
                    Foreground = BrushRed,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 20),
                });
                return;
            }

            if (!string.IsNullOrEmpty(preview.ScreenshotUrl))
            {
                byte[]? bytes = await HttpService.Instance.GetBytesAsync(preview.ScreenshotUrl, 10).ConfigureAwait(true);
                Bitmap? bitmap = bytes is null ? null : LoadImage(bytes);
                if (bitmap is not null)
                {
                    _contentPanel.Children.Add(new Image
                    {
                        Source = bitmap,
                        MaxHeight = 130,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 12),
                    });
                }
            }

            var factsGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };
            int row = 0;

            void AddFact(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                factsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var lbl = new TextBlock { Text = label, FontSize = 11.5, Foreground = BrushDim, Margin = new Thickness(0, 2, 12, 2) };
                var val = new TextBlock { Text = value, FontSize = 11.5, Foreground = BrushHeader, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(lbl, row);
                Grid.SetColumn(lbl, 0);
                Grid.SetRow(val, row);
                Grid.SetColumn(val, 1);
                factsGrid.Children.Add(lbl);
                factsGrid.Children.Add(val);
                row++;
            }

            AddFact(LocalizationService.T(Str.Preview_Label_BasedOn), preview.BasedOn);
            AddFact(LocalizationService.T(Str.Preview_Label_Desktop), preview.Desktop);
            AddFact(LocalizationService.T(Str.Preview_Label_Origin), preview.Origin);
            AddFact(LocalizationService.T(Str.Preview_Label_Architecture), preview.Architecture);
            if (preview.IsActive.HasValue)
                AddFact(LocalizationService.T(Str.Preview_Label_Status), LocalizationService.T(preview.IsActive.Value ? Str.Preview_Status_Active : Str.Preview_Status_Inactive));
            if (preview.PopularityRank > 0)
                AddFact(LocalizationService.T(Str.Preview_Label_Popularity), string.Format(LocalizationService.T(Str.Preview_PopularityValue), preview.PopularityRank, preview.PopularityHitsPerDay));
            _contentPanel.Children.Add(factsGrid);

            if (!string.IsNullOrWhiteSpace(preview.Description))
            {
                _contentPanel.Children.Add(new Border
                {
                    Background = BrushCard,
                    BorderBrush = BrushBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 12, 0, 12),
                    Padding = new Thickness(10),
                    Child = new TextBlock { Text = preview.Description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = BrushHeader },
                });
            }

            if (_tags.Count > 0)
            {
                var tagsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                foreach (string tag in _tags)
                {
                    tagsPanel.Children.Add(new Border
                    {
                        Background = BrushLBlue,
                        CornerRadius = new CornerRadius(10),
                        Margin = new Thickness(0, 0, 6, 6),
                        Padding = new Thickness(8, 3, 8, 3),
                        Child = new TextBlock { Text = tag, FontSize = 10, Foreground = BrushHeader },
                    });
                }
                _contentPanel.Children.Add(tagsPanel);
            }
        }

        private static Bitmap? LoadImage(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch { return null; }
        }
    }
}
