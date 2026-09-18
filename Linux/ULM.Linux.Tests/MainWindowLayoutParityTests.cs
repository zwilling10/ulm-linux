using System.IO;
using Avalonia;
using ULM.Linux.Views;
using Xunit;

namespace ULM.Linux.Tests;

public sealed class MainWindowLayoutParityTests
{
    [Fact]
    public void MainIsoTableUsesWindowsColumnLayout()
    {
        string xaml = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml", AppContext.BaseDirectory));

        Assert.Contains("ColumnDefinitions=\"36,30,388,130,150,150,30\"", xaml);
        Assert.Contains("Grid.Column=\"1\" Width=\"16\" Height=\"16\"", xaml);
        Assert.Contains("Grid.Column=\"6\" Content=\"🔧\"", xaml);
    }

    [Fact]
    public void UliButtonUsesWindowsGridPlacement()
    {
        string xaml = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml", AppContext.BaseDirectory));

        Assert.Contains("x:Name=\"UliButton\" Grid.Row=\"0\" Grid.RowSpan=\"4\"", xaml);
        Assert.Contains("HorizontalAlignment=\"Right\" VerticalAlignment=\"Bottom\"", xaml);
        Assert.Contains("Margin=\"0,0,20,12\"", xaml);
    }

    [Fact]
    public void MainWindowWiresUliButtonViaFindControl()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml.cs", AppContext.BaseDirectory));

        Assert.Contains("FindControl<AssistantAvatarButton>(\"UliButton\")", code);
        Assert.DoesNotContain("UliButton.GetLanguage", code);
    }

    [Fact]
    public void UliChatOpensBottomRightOfOwnerLikeWindows()
    {
        var position = AssistantAvatarButton.ComputeBottomRightPosition(
            new PixelPoint(20, 30),
            ownerWidth: 1160,
            ownerHeight: 750,
            chatWidth: 640,
            chatHeight: 480);

        Assert.Equal(new PixelPoint(524, 284), position);
    }

    [Fact]
    public void IsoSearchTakeOverStartsNormalDownloadFlow()
    {
        string code = File.ReadAllText(Path.GetFullPath("../../../../Views/MainWindow.axaml.cs", AppContext.BaseDirectory));

        Assert.Contains("StartDownloadFlowAsync(autoDownloadQueue", code);
        Assert.Contains("RunHealthCheckCommand.Execute(null)", code);
    }
}
