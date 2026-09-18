using Avalonia;
using Xunit;

namespace ULM.Linux.Tests;

public sealed class DistroPreviewDialogLayoutTests
{
    [Fact]
    public void PreviewOpensRightOfSearchWhenSpaceAllows()
    {
        var position = Views.DistroPreviewDialog.ComputeDockedPosition(
            new PixelPoint(100, 80),
            ownerWidth: 640,
            previewWidth: 380,
            new PixelRect(0, 0, 1600, 900));

        Assert.Equal(new PixelPoint(748, 80), position);
    }

    [Fact]
    public void PreviewOpensLeftOfSearchWhenRightSideWouldOverlapScreenEdge()
    {
        var position = Views.DistroPreviewDialog.ComputeDockedPosition(
            new PixelPoint(900, 80),
            ownerWidth: 640,
            previewWidth: 380,
            new PixelRect(0, 0, 1280, 900));

        Assert.Equal(new PixelPoint(512, 80), position);
    }
}
