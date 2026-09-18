using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using ULM.Core.Services;
using ULM.Infrastructure;
using ULM.Linux.ViewModels;
using ULM.Linux.Views;

namespace ULM.Linux
{
    public partial class App : Application
    {
        private DispatcherTimer? _drivePollTimer;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            Directory.CreateDirectory(LinuxPaths.ConfigDir);
            // Optionales, per SettingsDialog gesetztes Arbeitsverzeichnis (ISOs/DB/Log/Cache)
            // überschreibt den XDG-Standardpfad — fehlt der Schlüssel (Standardfall), bleibt
            // exakt das bisherige Verhalten (LinuxPaths.DataDir).
            string baseDir = IniService.Read(LinuxPaths.SettingsIni, "App", "BaseDirectory", LinuxPaths.DataDir);
            AppPaths.Instance.Apply(baseDir);
            LocalizationService.Initialize(LinuxPaths.SettingsIni);
            IsoDatabaseService.Instance.Load();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewModel = new LinuxMainViewModel(IsoDatabaseService.Instance, AppPaths.Instance.DownloadDir);
                var mainWindow = new MainWindow { DataContext = viewModel };
                desktop.MainWindow = mainWindow;

                // Nutzerwunsch: modales "Bitte warten"-Fenster mit Fortschrittsbalken während des
                // automatischen Start-Checks (Versionscheck, dann URL-Check — RunStartupChecksAsync)
                // — der Anwender soll die App erst benutzen können, wenn beide Phasen durch sind.
                // Erst NACH dem tatsächlichen Anzeigen des Hauptfensters starten (Opened-Event),
                // sonst hat der Dialog noch kein sichtbares Owner-Fenster für ShowDialog().
                //
                // BUGFIX (Nutzerfund 2026-09-16): der USB-Stick-Poll lief bisher SOFORT parallel
                // zum Start-Check — bei bereits eingestecktem Ventoy-Stick poppten dessen Dialoge
                // (unbekannte ISOs, veraltete Duplikate, neuere Version) gleichzeitig mit dem
                // Online-Scan-Fenster auf, komplett durcheinander. Stick-Polling (Timer + erster
                // Aufruf) startet jetzt ERST, wenn der Start-Check abgeschlossen ist — danach
                // laufen auch die einzelnen Stick-Dialoge sequenziell nacheinander (siehe
                // ClassifyStickFindings/Func<...,Task>-Umstellung in LinuxMainViewModel).
                mainWindow.Opened += (_, _) =>
                {
                    var startupDialog = new StartupCheckDialog(viewModel);

                    // Nutzerwunsch (2026-09-17): eigenes, NICHT-blockierendes "Bitte Geduld"-Popup
                    // während jedes Stick-Scans (egal ob der Stick schon beim Start steckte oder
                    // erst während der laufenden Sitzung eingesteckt wird — beide Fälle laufen über
                    // denselben UsbScanActive-Zustand in PollDrivesAsync/ScanConnectedStickAsync).
                    // Bewusst dieselbe Dialogklasse wie oben (nur anderer Text/andere Bindings) statt
                    // einer zweiten, fast identischen Fensterklasse. Show() statt ShowDialog(), damit
                    // die App währenddessen bedienbar bleibt.
                    StartupCheckDialog? stickScanDialog = null;
                    viewModel.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(viewModel.UsbScanActive)) return;
                        if (viewModel.UsbScanActive)
                        {
                            stickScanDialog = new StartupCheckDialog(viewModel, LocalizationService.T(Str.Msg_PleaseBePatient),
                                nameof(viewModel.ScanHintText), nameof(viewModel.UsbScanPercent));
                            stickScanDialog.Show(mainWindow);
                        }
                        else
                        {
                            stickScanDialog?.Close();
                            stickScanDialog = null;
                        }
                    };

                    async void OnCompleted()
                    {
                        viewModel.StartupChecksCompleted -= OnCompleted;
                        startupDialog.Close();

                        _drivePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                        _drivePollTimer.Tick += async (_, _) => await viewModel.PollDrivesAsync();
                        _drivePollTimer.Start();
                        await viewModel.PollDrivesAsync();
                    }
                    viewModel.StartupChecksCompleted += OnCompleted;
                    _ = startupDialog.ShowDialog(mainWindow);
                    _ = viewModel.RunStartupChecksAsync();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
