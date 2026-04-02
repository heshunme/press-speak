using HsAsrDictation.Services;

namespace HsAsrDictation.Overlay;

public sealed class DictationOverlayController
{
    private readonly IStatusOverlayService _statusOverlayService;

    public DictationOverlayController(IStatusOverlayService statusOverlayService)
    {
        _statusOverlayService = statusOverlayService;
    }

    public void Update(DictationState state)
    {
        if (state == DictationState.Recording)
        {
            _statusOverlayService.Show(state.ToDisplayText());
            return;
        }

        _statusOverlayService.Hide();
    }
}
