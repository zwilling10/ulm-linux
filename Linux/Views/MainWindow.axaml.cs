using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;

namespace ULM.Linux.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContextChanged += (_, _) => WireViewModel();
        }

        private LinuxMainViewModel? _vm;

        private void WireViewModel()
        {
            if (_vm is not null) _vm.HealthCheckCompleted -= OnHealthCheckCompleted;
            _vm = DataContext as LinuxMainViewModel;
            if (_vm is not null) _vm.HealthCheckCompleted += OnHealthCheckCompleted;
        }

        // Bewusst Code-behind statt reines MVVM-Command (gleiches Muster wie Windows'
        // MainWindow.xaml.cs BtnGitHubToken_Click/HealthCheckCompleted-Handler) — Dialoge brauchen
        // ein Fenster als Owner, das gehört in die View, nicht ins ViewModel.
        private async void BtnGitHubToken_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            var dlg = new GitHubTokenDialog(_vm.GitHubToken);
            string? result = await dlg.ShowDialog<string?>(this);
            if (result is not null) _vm.GitHubToken = result;
        }

        private async void BtnSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            await new SettingsDialog(_vm).ShowDialog(this);
        }

        // DB-Gesundheitscheck selbst läuft komplett über RunHealthCheckCommand (MVVM-Binding in
        // MainWindow.axaml, braucht kein Fenster als Owner) — nur das Ergebnis-Dialog-Öffnen
        // danach braucht Code-behind (Owner-Fenster).
        private void OnHealthCheckCompleted(System.Collections.Generic.IReadOnlyList<ULM.Core.Workers.VersionCheckEntryResult> results)
        {
            var dlg = new DbHealthCheckDialog(results);
            _ = dlg.ShowDialog(this);
        }

        // Arbeitet direkt gegen IsoDatabaseService.Instance statt vm._db (siehe Plan/Windows-
        // Vorbild BtnEditDb_Click) — MoveUp/MoveDown existieren nur auf der konkreten Klasse.
        private async void BtnDatabase_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null || _vm.IsBusy) return;
            var dlg = new IsoListDialog(ULM.Core.Services.IsoDatabaseService.Instance);
            await dlg.ShowDialog<bool>(this);
            _vm.Refresh();
            if (dlg.AnyEntryAdded) _vm.RunHealthCheckCommand.Execute(null);
        }

        // Windows-Vorbild: MainWindow.xaml.cs BtnSearch_Click. Bewusst OHNE den dortigen
        // Auto-Download-Teil (ToDownload -> sofortiger Mehrfach-Download) — Linux' DownloadCommand
        // kennt nur SelectedRow (Single-Select), ein Massen-Download über IsSelected-Zeilen ist
        // kein bestehendes Feature (siehe Phase-A/B-4-Plan). Übernommene "sofort herunterladen"-
        // Einträge werden stattdessen nur in der Hauptliste vorausgewählt.
        private async void BtnSearchIso_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm is null) return;
            var dlg = new IsoSearchDialog();
            await dlg.ShowDialog<bool>(this);
            if (dlg.AddedEntries.Count == 0) return;

            foreach (var entry in dlg.AddedEntries) ULM.Core.Services.IsoDatabaseService.Instance.Add(entry);
            ULM.Core.Services.IsoDatabaseService.Instance.Save();
            _vm.Refresh();
            _vm.LogEntries.Add(string.Format(LocalizationService.T(Str.Log_IsosAddedFromOnlineSearch), dlg.AddedEntries.Count));
            // Frisch aus der Online-Suche übernommene Einträge haben nie eine geprüfte Url — wie bei
            // Datenbank-Neuanlagen lohnt sich hier der volle Gesundheitscheck sofort.
            _vm.RunHealthCheckCommand.Execute(null);

            if (dlg.ToDownload.Count > 0)
                foreach (var row in _vm.Rows)
                    if (dlg.ToDownload.Contains(row.Entry)) row.IsSelected = true;
        }
    }
}
