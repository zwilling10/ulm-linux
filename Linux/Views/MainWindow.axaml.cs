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
            if (_vm is not null)
            {
                _vm.HealthCheckCompleted += OnHealthCheckCompleted;
                // Windows-Pendant: MainWindow.xaml.cs' `_vm.ConfirmSlowDownload = (name, host) =>
                // MessageBox.Show(...)`. DownloadWorker ruft das synchron von einem Hintergrund-
                // Thread (dem jeweils langsamen Download-Slot) auf — InvokeAsync(Func<Task<bool>>)
                // marschalliert Öffnen+Warten auf den UI-Thread, GetAwaiter().GetResult() blockiert
                // dabei nur den aufrufenden Slot-Thread, nicht den UI-Thread selbst (der pumpt die
                // Dialog-Nachrichtenschleife währenddessen normal weiter).
                _vm.ConfirmSlowDownload = (name, host) =>
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmDialog.ShowAsync(this,
                        LocalizationService.T(Str.Msg_SlowDownload_Title),
                        string.Format(LocalizationService.T(Str.Msg_SlowDownload_Body), name, host)))
                    .GetAwaiter().GetResult();
            }
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

        private async void BtnHelp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            await new HelpDialog().ShowDialog(this);

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

        // Windows-Vorbild: MainWindow.xaml.cs BtnSearch_Click. Seit der Warteschlangen-Phase
        // (2026-09-04) markiert dies die übernommenen "sofort herunterladen"-Einträge nur als
        // IsSelected (angehakt) — ein Klick auf "Herunterladen" lädt sie dann als echte
        // Mehrfach-Warteschlange über DownloadQueueAsync(), kein separater Auto-Download-Zweig
        // mehr nötig wie unter Windows.
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
