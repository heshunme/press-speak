using HsAsrDictation.Overlay;
using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class DictationOverlayControllerTests
{
    [Fact]
    public void Update_Recording_ShowsOverlayWithRecordingText()
    {
        var overlay = new FakeStatusOverlayService();
        var controller = new DictationOverlayController(overlay);

        controller.Update(DictationState.Recording);

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal("录音中", overlay.LastShownText);
        Assert.Equal(0, overlay.HideCallCount);
    }

    [Theory]
    [InlineData(DictationState.Idle)]
    [InlineData(DictationState.Finalizing)]
    [InlineData(DictationState.Decoding)]
    [InlineData(DictationState.Inserting)]
    public void Update_NonRecording_HidesOverlay(DictationState state)
    {
        var overlay = new FakeStatusOverlayService();
        var controller = new DictationOverlayController(overlay);

        controller.Update(state);

        Assert.Equal(0, overlay.ShowCallCount);
        Assert.Equal(1, overlay.HideCallCount);
    }

    private sealed class FakeStatusOverlayService : IStatusOverlayService
    {
        public int ShowCallCount { get; private set; }

        public int HideCallCount { get; private set; }

        public string? LastShownText { get; private set; }

        public void Show(string text)
        {
            ShowCallCount++;
            LastShownText = text;
        }

        public void Hide()
        {
            HideCallCount++;
        }

        public void Dispose()
        {
        }
    }
}
