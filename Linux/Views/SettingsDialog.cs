using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;

namespace ULM.Linux.Views
{
    /// <summary>Ersetzt den bisher deaktivierten "⚙ Einstellungen"-Header-Button — Avalonia-Pendant
    /// zu Windows' konsolidiertem SetupDialog-"Lite"-Modus (siehe docs/superpowers/specs/
    /// 2026-07-23-settings-consolidation-design.md), aber mit auf Linux tatsächlich vorhandenen
    /// Optionen statt einer 1:1-Kopie (Windows' Modus/Autostart/Design-Karten haben auf Linux
    /// aktuell kein Äquivalent): Sprache (bereits bestehender Umschalter, zieht hierher um) +
    /// Arbeitsverzeichnis (neu — Windows bietet das nach dem Ersteinrichtungs-Dialog gar nicht
    /// mehr an, hier bewusst als späterer Änderungsweg ergänzt).
    /// Sammel-Muster wie beim Windows-Vorbild: ein "✔ Übernehmen"-Button wendet beides an.</summary>
    public sealed class SettingsDialog : Window
    {
        private readonly LinuxMainViewModel _vm;
        private AppLanguage _chosenLanguage;
        private string _chosenBaseDir;
        private readonly Button _deBtn;
        private readonly Button _enBtn;
        private readonly TextBlock _dirValueTb;

        public SettingsDialog(LinuxMainViewModel vm)
        {
            _vm = vm;
            _chosenLanguage = LocalizationService.Current;
            _chosenBaseDir = AppPaths.Instance.BaseDirectory;

            Title = LocalizationService.T(Str.Settings_DialogTitle);
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(20) };

            // ── Sprache ──────────────────────────────────────────────
            root.Children.Add(new TextBlock { Text = LocalizationService.T(Str.Settings_Card_Language), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var langRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            _deBtn = new Button { Content = "🇩🇪 Deutsch", MinWidth = 130 };
            _enBtn = new Button { Content = "🇬🇧 English", MinWidth = 130, Margin = new Thickness(8, 0, 0, 0) };
            _deBtn.Click += (_, _) => { _chosenLanguage = AppLanguage.German; UpdateLangButtons(); };
            _enBtn.Click += (_, _) => { _chosenLanguage = AppLanguage.English; UpdateLangButtons(); };
            langRow.Children.Add(_deBtn);
            langRow.Children.Add(_enBtn);
            root.Children.Add(langRow);
            UpdateLangButtons();

            // ── Arbeitsverzeichnis ───────────────────────────────────
            root.Children.Add(new TextBlock { Text = LocalizationService.T(Str.Settings_Card_WorkDir), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            _dirValueTb = new TextBlock { Text = _chosenBaseDir, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_dirValueTb);
            var chooseBtn = new Button { Content = LocalizationService.T(Str.Settings_Btn_ChooseFolder), Classes = { "ghost" } };
            chooseBtn.Click += OnChooseFolderClick;
            root.Children.Add(chooseBtn);
            root.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(Str.Settings_WorkDir_Hint), TextWrapping = TextWrapping.Wrap,
                FontSize = 10.5, Margin = new Thickness(0, 6, 0, 20),
            });

            // ── Übernehmen / Abbrechen ───────────────────────────────
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var apply = new Button { Content = LocalizationService.T(Str.Setup_Btn_Apply), Classes = { "primary" }, MinWidth = 110 };
            apply.Click += OnApplyClick;
            var cancel = new Button { Content = LocalizationService.T(Str.Db_Btn_Cancel), Classes = { "ghost" }, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (_, _) => Close();
            btns.Children.Add(apply);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            Content = root;
        }

        private void UpdateLangButtons()
        {
            _deBtn.Classes.Clear();
            _deBtn.Classes.Add(_chosenLanguage == AppLanguage.German ? "primary" : "ghost");
            _enBtn.Classes.Clear();
            _enBtn.Classes.Add(_chosenLanguage == AppLanguage.English ? "primary" : "ghost");
        }

        private async void OnChooseFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = LocalizationService.T(Str.Settings_Card_WorkDir), AllowMultiple = false });
            if (result.Count == 0) return;
            string? path = result[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            _chosenBaseDir = path;
            _dirValueTb.Text = path;
        }

        private async void OnApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_chosenLanguage != LocalizationService.Current) _vm.ToggleLanguageCommand.Execute(null);

            bool dirChanged = _chosenBaseDir != AppPaths.Instance.BaseDirectory;
            if (dirChanged)
            {
                IniService.Write(LinuxPaths.SettingsIni, "App", "BaseDirectory", _chosenBaseDir);
                bool restart = await ConfirmDialog.ShowAsync(this,
                    LocalizationService.T(Str.Settings_RestartConfirm_Title),
                    LocalizationService.T(Str.Settings_RestartConfirm_Message));
                if (restart)
                {
                    string? exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe)) Process.Start(exe);
                    Environment.Exit(0);
                    return;
                }
            }
            Close();
        }
    }
}
