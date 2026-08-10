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
            AppPaths.Instance.Apply(LinuxPaths.DataDir);
            LocalizationService.Initialize(LinuxPaths.SettingsIni);
            IsoDatabaseService.Instance.Load();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewModel = new LinuxMainViewModel(IsoDatabaseService.Instance, AppPaths.Instance.DownloadDir);
                desktop.MainWindow = new MainWindow { DataContext = viewModel };

                _drivePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                _drivePollTimer.Tick += async (_, _) => await viewModel.PollDrivesAsync();
                _drivePollTimer.Start();
                _ = viewModel.PollDrivesAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
