using HsAsrDictation.Overlay;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class OverlayPlacementTests
{
    [Fact]
    public void ComputeBottomCenter_PrimaryScreen_CentersHorizontallyAndAppliesBottomMargin()
    {
        var (left, top) = OverlayPlacement.ComputeBottomCenter(
            workLeft: 0, workTop: 0, workWidth: 1920, workHeight: 1080,
            windowWidth: 400, windowHeight: 100, bottomMargin: 48);

        Assert.Equal(760, left);
        Assert.Equal(932, top);
    }

    [Fact]
    public void ComputeBottomCenter_SecondaryScreenWithNegativeOrigin_StaysOnThatScreen()
    {
        var (left, top) = OverlayPlacement.ComputeBottomCenter(
            workLeft: -2560, workTop: 0, workWidth: 2560, workHeight: 1440,
            windowWidth: 400, windowHeight: 100, bottomMargin: 48);

        Assert.Equal(-1480, left);
        Assert.Equal(1292, top);
    }

    [Fact]
    public void ComputeBottomCenter_WindowWiderThanWorkArea_ClampsToLeftEdge()
    {
        var (left, _) = OverlayPlacement.ComputeBottomCenter(
            workLeft: 100, workTop: 50, workWidth: 800, workHeight: 600,
            windowWidth: 1000, windowHeight: 100, bottomMargin: 48);

        Assert.Equal(100, left);
    }

    [Fact]
    public void ComputeBottomCenter_WindowTallerThanWorkArea_ClampsToTopEdge()
    {
        var (_, top) = OverlayPlacement.ComputeBottomCenter(
            workLeft: 0, workTop: 50, workWidth: 800, workHeight: 600,
            windowWidth: 400, windowHeight: 700, bottomMargin: 48);

        Assert.Equal(50, top);
    }

    [Fact]
    public void ComputeBottomCenter_NegativeMargin_NeverExtendsBelowWorkArea()
    {
        var (_, top) = OverlayPlacement.ComputeBottomCenter(
            workLeft: 0, workTop: 0, workWidth: 1920, workHeight: 1080,
            windowWidth: 400, windowHeight: 100, bottomMargin: -20);

        Assert.Equal(980, top);
    }

    [Fact]
    public void ComputeBottomCenter_ScaledPhysicalInputs_ComputesInSameUnit()
    {
        // 150% DPI 下调用方应传入已换算的物理尺寸与边距（400x100 DIP、48 DIP → 600x150、72 物理像素）。
        var (left, top) = OverlayPlacement.ComputeBottomCenter(
            workLeft: 0, workTop: 0, workWidth: 1920, workHeight: 1080,
            windowWidth: 600, windowHeight: 150, bottomMargin: 72);

        Assert.Equal(660, left);
        Assert.Equal(858, top);
    }
}
