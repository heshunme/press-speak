using HsAsrDictation.Services;

namespace HsAsrDictation.Overlay;

public sealed class DictationOverlayController
{
    private readonly IStatusOverlayService _statusOverlayService;

    public DictationOverlayController(IStatusOverlayService statusOverlayService)
    {
        _statusOverlayService = statusOverlayService;
    }

    public void Update(DictationStatus status)
    {
        if (status.State == DictationState.Idle)
        {
            _statusOverlayService.Hide();
            return;
        }

        _statusOverlayService.Show(
            status.OverlayText,
            status.HasPreview ? status.PreviewText : null);
    }
}
