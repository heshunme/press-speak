using HsAsrDictation.Overlay;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class DictationOverlayControllerTests
{
    [Fact]
    public void Update_Recording_ShowsOverlayWithPreview()
    {
        var overlay = new FakeStatusOverlayService();
        var controller = new DictationOverlayController(overlay);

        controller.Update(new DictationStatus
        {
            State = DictationState.Recording,
            Mode = RecognitionMode.Hybrid,
            OverlayText = "录音中",
            PreviewText = "实时文本"
        });

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal("录音中", overlay.LastShownStatusText);
        Assert.Equal("实时文本", overlay.LastShownPreviewText);
        Assert.Equal(0, overlay.HideCallCount);
    }

    [Theory]
    [InlineData(DictationState.Recording)]
    [InlineData(DictationState.Finalizing)]
    [InlineData(DictationState.Decoding)]
    [InlineData(DictationState.Inserting)]
    public void Update_NonIdle_ShowsOverlay(DictationState state)
    {
        var overlay = new FakeStatusOverlayService();
        var controller = new DictationOverlayController(overlay);

        controller.Update(new DictationStatus
        {
            State = state,
            Mode = RecognitionMode.Hybrid,
            OverlayText = state.ToDisplayText()
        });

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal(0, overlay.HideCallCount);
    }

    [Fact]
    public void Update_Idle_HidesOverlay()
    {
        var overlay = new FakeStatusOverlayService();
        var controller = new DictationOverlayController(overlay);

        controller.Update(new DictationStatus
        {
            State = DictationState.Idle,
            Mode = RecognitionMode.Hybrid,
            OverlayText = DictationState.Idle.ToDisplayText()
        });

        Assert.Equal(0, overlay.ShowCallCount);
        Assert.Equal(1, overlay.HideCallCount);
    }

    private sealed class FakeStatusOverlayService : IStatusOverlayService
    {
        public int ShowCallCount { get; private set; }

        public int HideCallCount { get; private set; }

        public string? LastShownStatusText { get; private set; }

        public string? LastShownPreviewText { get; private set; }

        public void Show(string statusText, string? previewText)
        {
            ShowCallCount++;
            LastShownStatusText = statusText;
            LastShownPreviewText = previewText;
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
