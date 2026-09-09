using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Linux-Hilfe mit den verfügbaren Programm- und Expertenfunktionen.</summary>
    public sealed class HelpDialog : Window
    {
        private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
        private static readonly IBrush BrushDim    = new SolidColorBrush(Color.Parse("#8BA3BE"));
        private static readonly IBrush BrushMid    = new SolidColorBrush(Color.Parse("#B8C9DC"));
        private static readonly IBrush BrushBlue   = new SolidColorBrush(Color.Parse("#3AAEEF"));
        private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#336B9E"));
        private static readonly IBrush BrushGreen  = new SolidColorBrush(Color.Parse("#2ECC71"));
        private static readonly IBrush BrushAmber  = new SolidColorBrush(Color.Parse("#F39C12"));
        private static readonly IBrush BrushRed    = new SolidColorBrush(Color.Parse("#E74C3C"));
        private static readonly IBrush BrushTeal   = new SolidColorBrush(Color.Parse("#1ABC9C"));
        private static readonly IBrush BrushCard   = new SolidColorBrush(Color.Parse("#132A44"));

        public HelpDialog()
        {
            Title = LocalizationService.T(Str.Help_Title);
            Width = 880; Height = 660;
            MinWidth = 680; MinHeight = 420;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("178,*") };
            var tocPanel = new StackPanel { Margin = new Thickness(14, 20, 10, 10) };
            var tocHost = new Border
            {
                Background = BrushCard, BorderBrush = BrushBorder, BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = tocPanel,
                },
            };
            Grid.SetColumn(tocHost, 0);
            body.Children.Add(tocHost);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(24, 20, 24, 10),
            };
            Grid.SetColumn(scroll, 1);
            body.Children.Add(scroll);
            Grid.SetRow(body, 0);
            root.Children.Add(body);

            var content = new StackPanel();

            void AddSection(string title, string navLabel)
            {
                var section = MakeSection(title);
                content.Children.Add(section);
                tocPanel.Children.Add(MakeNavLink(navLabel, scroll, section));
            }

            content.Children.Add(MakeTitle(Constants.AppFullTitle));
            content.Children.Add(MakeSub(LocalizationService.T(Str.Help_Subtitle)));
            content.Children.Add(Spacer(16));

            tocPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(Str.Help_NavHeading), FontSize = 9.5, FontWeight = FontWeight.Bold,
                Foreground = BrushDim, Margin = new Thickness(6, 0, 0, 8),
            });

            // ── Übersicht ──────────────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_Overview_Title), LocalizationService.T(Str.Help_Sec_Overview_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_Overview_Body)));
            content.Children.Add(Spacer());

            // Programmstart
            AddSection(LocalizationService.T(Str.Help_Sec_Startup_Title), LocalizationService.T(Str.Help_Sec_Startup_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_Startup_Intro)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_Autostart_Label),
                LocalizationService.Current == AppLanguage.German
                    ? "Unter Einstellungen einschalten: ULM startet dann bei der Anmeldung an dieser Linux-Sitzung. Dort auch wieder ausschaltbar."
                    : "Enable in Settings to start ULM when you log into your Linux session. Disable it there at any time."));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_OnlineCheck_Label), LocalizationService.T(Str.Help_Item_OnlineCheck_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_UsbScan_Label), LocalizationService.T(Str.Help_Item_UsbScan_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_FileMaintenance_Label), LocalizationService.T(Str.Help_Item_FileMaintenance_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_UpdateCheck_Label), LocalizationService.T(Str.Help_Item_UpdateCheck_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_WhatsNew_Label), LocalizationService.T(Str.Help_Item_WhatsNew_Body)));
            content.Children.Add(Spacer());

            AddSection(LocalizationService.Current == AppLanguage.German ? "Einstellungen und Expertenmodus" : "Settings and expert mode",
                LocalizationService.Current == AppLanguage.German ? "Einstellungen" : "Settings");
            content.Children.Add(MakeText(LocalizationService.Current == AppLanguage.German
                ? "Einstellungen bietet System-, helles und dunkles Design. Der Expertenmodus blendet zusätzliche Werkzeuge, Ventoy-Einrichtung und den Aktivitätsverlauf ein. Im einfachen Modus bleiben ISO-Auswahl und Downloads erreichbar."
                : "Settings provides System, Light and Dark themes. Expert mode shows additional tools, Ventoy setup and activity history. Simple mode keeps ISO selection and downloads accessible."));
            content.Children.Add(MakeText(LocalizationService.Current == AppLanguage.German
                ? "Quelle reparieren: genau eine ISO markieren, eine neue Downloadquelle suchen oder eintragen und anschließend erneut herunterladen. Der gleiche Einstieg erscheint bei fehlgeschlagenen Downloads."
                : "Repair source: select exactly one ISO, search for or enter a new download source, then retry. The same action is available for failed downloads."));
            content.Children.Add(Spacer());

            // ── Bedienung ──────────────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_Usage_Title), LocalizationService.T(Str.Help_Sec_Usage_Nav));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SelectDownload_Label), LocalizationService.T(Str.Help_Item_SelectDownload_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_CategoryCheckbox_Label), LocalizationService.T(Str.Help_Item_CategoryCheckbox_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_DoubleClick_Label), LocalizationService.T(Str.Help_Item_DoubleClick_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_MouseoverTooltip_Label), LocalizationService.T(Str.Help_Item_MouseoverTooltip_Body)));
            content.Children.Add(Spacer());

            // ── Farben & Symbole ───────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_Colors_Title), LocalizationService.T(Str.Help_Sec_Colors_Nav));
            content.Children.Add(MakeSubhead(LocalizationService.T(Str.Help_Subhead_TextColors)));
            content.Children.Add(MakeColorItem(BrushGreen, LocalizationService.T(Str.Help_Color_Green_Label), LocalizationService.T(Str.Help_Color_Green_Body)));
            content.Children.Add(MakeColorItem(BrushAmber, LocalizationService.T(Str.Help_Color_Orange_Label), LocalizationService.T(Str.Help_Color_Orange_Body)));
            content.Children.Add(MakeColorItem(BrushRed, LocalizationService.T(Str.Help_Color_Red_Label), LocalizationService.T(Str.Linux_Help_Color_Red_Body)));
            content.Children.Add(MakeColorItem(BrushTeal, LocalizationService.T(Str.Help_Color_Teal_Label), LocalizationService.T(Str.Help_Color_Teal_Body)));
            content.Children.Add(MakeColorItem(BrushMid, LocalizationService.T(Str.Help_Color_Blue_Label), LocalizationService.T(Str.Help_Color_Blue_Body)));
            content.Children.Add(MakeColorItem(BrushDim, LocalizationService.T(Str.Help_Color_Gray_Label), LocalizationService.T(Str.Help_Color_Gray_Body)));
            content.Children.Add(MakeColorItem(BrushHeader, LocalizationService.T(Str.Help_Color_Dark_Label), LocalizationService.T(Str.Help_Color_Dark_Body)));
            content.Children.Add(Spacer(6));
            content.Children.Add(MakeSubhead(LocalizationService.T(Str.Help_Subhead_Columns)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_ColLocal_Label), LocalizationService.T(Str.Help_Item_ColLocal_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_ColOnStick_Label), LocalizationService.T(Str.Help_Item_ColOnStick_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_ColCurrent_Label), LocalizationService.T(Str.Help_Item_ColCurrent_Body)));
            content.Children.Add(Spacer(6));
            content.Children.Add(MakeSubhead(LocalizationService.T(Str.Help_Subhead_HashSymbol)));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_HashSymbol_Body)));
            content.Children.Add(Spacer(6));
            content.Children.Add(MakeSubhead(LocalizationService.T(Str.Help_Subhead_NameSymbols)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SymbolImported_Label), LocalizationService.T(Str.Help_Item_SymbolImported_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SymbolUrlOk_Label), LocalizationService.T(Str.Help_Item_SymbolUrlOk_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SymbolUrlFail_Label), LocalizationService.T(Str.Help_Item_SymbolUrlFail_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SymbolNewVersion_Label), LocalizationService.T(Str.Help_Item_SymbolNewVersion_Body)));
            content.Children.Add(Spacer(6));
            content.Children.Add(MakeSubhead(LocalizationService.T(Str.Help_Subhead_CategorySymbols)));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_CategorySymbols_Body)));
            content.Children.Add(Spacer());

            // ── Protokoll-Symbole ─────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_LogSymbols_Title), LocalizationService.T(Str.Help_Sec_LogSymbols_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_LogSymbols_Body)));
            content.Children.Add(Spacer());

            // ── ISO suchen ─────────────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_IsoSearch_Title), LocalizationService.T(Str.Help_Sec_IsoSearch_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_IsoSearch_Intro)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_Newest_Label), LocalizationService.T(Str.Help_Item_Newest_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_Popular_Label), LocalizationService.T(Str.Help_Item_Popular_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_LiveOnly_Label), LocalizationService.T(Str.Help_Item_LiveOnly_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_AlreadyInDb_Label), LocalizationService.T(Str.Help_Item_AlreadyInDb_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_AdoptAndDownload_Label), LocalizationService.T(Str.Help_Item_AdoptAndDownload_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_RefreshCache_Label), LocalizationService.T(Str.Help_Item_RefreshCache_Body)));
            content.Children.Add(Spacer());

            // ── Download ───────────────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_Download_Title), LocalizationService.T(Str.Help_Sec_Download_Nav));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_StorageLocation_Label), LocalizationService.T(Str.Linux_Help_StorageLocation_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_PipelineMode_Label), LocalizationService.T(Str.Help_Item_PipelineMode_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_MirrorRace_Label), LocalizationService.T(Str.Help_Item_MirrorRace_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_SpeedGuard_Label), LocalizationService.T(Str.Help_Item_SpeedGuard_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_FasterButton_Label), LocalizationService.T(Str.Help_Item_FasterButton_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_EtaRemaining_Label), LocalizationService.T(Str.Help_Item_EtaRemaining_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_VerifyIntegrity_Label), LocalizationService.T(Str.Help_Item_VerifyIntegrity_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_FreeSpaceCheck_Label), LocalizationService.T(Str.Help_Item_FreeSpaceCheck_Body)));
            content.Children.Add(Spacer());

            // ── USB-Stick (ohne "Verpasste Kopien nachholen" — kein eigener Button auf Linux) ──
            AddSection(LocalizationService.T(Str.Help_Sec_UsbStick_Title), LocalizationService.T(Str.Help_Sec_UsbStick_Nav));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_CatchUpCopies_Label),
                LocalizationService.Current == AppLanguage.German
                    ? "Bietet lokal vollständig vorhandene, auf dem gewählten Stick fehlende ISOs zum Kopieren an. Der Expertenbereich enthält auch einen eigenen Button."
                    : "Offers to copy locally complete ISOs missing from the selected USB drive. Expert mode also provides a dedicated button."));
            content.Children.Add(MakeText(LocalizationService.Current == AppLanguage.German
                ? "Die Integritätsprüfung lässt sich mit Abbrechen stoppen. Beschädigte oder unvollständige Stick-Dateien können anschließend gezielt ausgewählt und entfernt werden."
                : "Use Cancel to stop an integrity check. Damaged or incomplete USB files can then be selected and removed."));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_WhatIsVentoy_Label), LocalizationService.T(Str.Help_Item_WhatIsVentoy_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_InstallUpdateVentoy_Label), LocalizationService.T(Str.Linux_Help_InstallUpdateVentoy_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_MultipleSticks_Label), LocalizationService.T(Str.Help_Item_MultipleSticks_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_BootMenu_Label), LocalizationService.T(Str.Help_Item_BootMenu_Body)));
            content.Children.Add(Spacer());

            // ── Datenmüll-Schutz ──────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_JunkProtection_Title), LocalizationService.T(Str.Help_Sec_JunkProtection_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_JunkProtection_Intro)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_WhenChecked_Label), LocalizationService.T(Str.Help_Item_WhenChecked_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_HowChecked_Label), LocalizationService.T(Str.Help_Item_HowChecked_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_JunkInFolder_Label), LocalizationService.T(Str.Help_Item_JunkInFolder_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_JunkOnStick_Label), LocalizationService.T(Str.Help_Item_JunkOnStick_Body)));
            content.Children.Add(Spacer());

            // ── ISO-Import ────────────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_IsoImport_Title), LocalizationService.T(Str.Help_Sec_IsoImport_Nav));
            content.Children.Add(MakeText(LocalizationService.T(Str.Help_IsoImport_Intro)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_NameCategoryUrl_Label), LocalizationService.T(Str.Help_Item_NameCategoryUrl_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_FolderStructure_Label), LocalizationService.T(Str.Help_Item_FolderStructure_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_DuplicateProtection_Label), LocalizationService.T(Str.Help_Item_DuplicateProtection_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_StayUpToDate_Label), LocalizationService.T(Str.Help_Item_StayUpToDate_Body)));
            content.Children.Add(Spacer());

            // ── Protokoll / Diagnose ────────────────────────────────────────
            AddSection(LocalizationService.T(Str.Help_Sec_Diagnostics_Title), LocalizationService.T(Str.Help_Sec_Diagnostics_Nav));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_DownloadUrl_Label), LocalizationService.T(Str.Help_Item_DownloadUrl_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_LogFile_Label), LocalizationService.T(Str.Help_Item_LogFile_Body)));
            content.Children.Add(MakeItem(LocalizationService.T(Str.Help_Item_LogRotation_Label), LocalizationService.T(Str.Help_Item_LogRotation_Body)));

            scroll.Content = content;

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 8, 24, 16) };
            var btnOk = new Button { Content = LocalizationService.T(Str.Help_Btn_Close), Width = 130, Classes = { "primary" } };
            btnOk.Click += (_, _) => Close();
            btnRow.Children.Add(btnOk);
            Grid.SetRow(btnRow, 1);
            root.Children.Add(btnRow);

            Content = root;
            KeyDown += (_, e) => { if (e.Key == Key.Enter || e.Key == Key.Escape) Close(); };
        }

        // ── UI-Hilfsmethoden (1:1 Struktur wie Windows-Original) ───────────

        private TextBlock MakeTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = BrushHeader, Margin = new Thickness(0, 0, 0, 4) };

        private TextBlock MakeSub(string text) => new() { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = BrushDim };

        private Border MakeSection(string title)
        {
            var lbl = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = BrushBlue, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            panel.Children.Add(lbl);
            return new Border { Child = panel, BorderBrush = BrushBorder, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(0, 0, 0, 4) };
        }

        // Klickbare Sprungmarke — scrollt die Ziel-Sektion an den oberen Rand des Inhaltsbereichs.
        private Button MakeNavLink(string text, ScrollViewer scroll, Control target)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = BrushHeader, FontSize = 11,
                Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 1),
            };
            btn.Click += (_, _) =>
            {
                if (scroll.Content is not Visual scrollContent) return;
                Point? p = target.TranslatePoint(new Point(0, 0), scrollContent);
                if (p is null) return;
                scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, p.Value.Y - 4));
            };
            return btn;
        }

        private TextBlock MakeSubhead(string text) => new() { Text = text, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = BrushHeader, Margin = new Thickness(0, 4, 0, 6) };

        private Control MakeItem(string label, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), ColumnDefinitions = new ColumnDefinitions("155,*") };
            var lbl = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 11.5, Foreground = BrushHeader, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            var txt = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = BrushMid, LineHeight = 18 };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(txt, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(txt);
            return grid;
        }

        private Control MakeColorItem(IBrush swatchColor, string label, string description)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 7), ColumnDefinitions = new ColumnDefinitions("16,115,*") };
            var dot = new Ellipse { Width = 12, Height = 12, Fill = swatchColor, Margin = new Thickness(12, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
            var lbl = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = swatchColor, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0) };
            var desc = new TextBlock { Text = description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = BrushMid, LineHeight = 18 };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(lbl, 1);
            Grid.SetColumn(desc, 2);
            grid.Children.Add(dot);
            grid.Children.Add(lbl);
            grid.Children.Add(desc);
            return grid;
        }

        private TextBlock MakeText(string text) => new() { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = BrushMid, Margin = new Thickness(12, 0, 0, 8), LineHeight = 18 };

        private static Control Spacer(double h = 8) => new Border { Height = h };
    }
}
