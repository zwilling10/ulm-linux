using System.IO;
using ULM.Linux.Views;
using Xunit;

namespace ULM.Linux.Tests;

public sealed class StartupOnlineCheckTests
{
    [Fact]
    public void WaitDialog_UsesRequestedMessageAndSpinner()
    {
        Assert.Equal("Bitte warten, ULM prüft die Verfügbarkeit/Erreichbarkeit  und Version`s Aktualisierung der Distros automatisch",
            StartupOnlineCheckDialog.WaitMessage);
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/StartupOnlineCheckDialog.cs", AppContext.BaseDirectory));
        Assert.Contains("DispatcherTimer", code);
        Assert.Contains("RotateTransform", code);
        Assert.Contains("ProgressBar", code);
        Assert.Contains("SetProgress", code);
        Assert.Contains("\"0%\"", code);
    }

    [Fact]
    public void MainWindow_StartsModalOnlineCheckAfterOpen()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml.cs", AppContext.BaseDirectory));

        Assert.Contains("RunStartupOnlineChecksWithDialogAsync", code);
        Assert.Contains("StartupOnlineCheckDialog", code);
        Assert.Contains("Keine aktive Internetverbindung", code);
    }

    [Fact]
    public void App_DoesNotStartBackgroundAutoVersionCheckDirectly()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../App.axaml.cs", AppContext.BaseDirectory));

        Assert.DoesNotContain("TriggerAutoVersionCheckAsync", code);
    }

    [Fact]
    public void ViewModel_MapsStartupProgressToUrlAndVersionPhases()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../ViewModels/LinuxMainViewModel.cs", AppContext.BaseDirectory));

        Assert.Contains("Action<int>? progress", code);
        Assert.Contains("progress?.Invoke(p / 2)", code);
        Assert.Contains("progress?.Invoke(50 + p / 2)", code);
    }
}
