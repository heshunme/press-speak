using HsAsrDictation.Services;
using HsAsrDictation.Views;

namespace HsAsrDictation.Overlay;

public sealed class StatusOverlayService : IStatusOverlayService
{
    private readonly StatusOverlayWindow _window;

    public StatusOverlayService()
    {
        _window = new StatusOverlayWindow();
    }

    public void Show(string statusText, string? previewText)
    {
        UiThreadHelper.RunOnUiThread(() =>
        {
            _window.SetMessage(statusText, previewText);

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _window.UpdatePosition();
        });
    }

    public void Hide()
    {
        UiThreadHelper.RunOnUiThread(() =>
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }
        });
    }

    public void Dispose()
    {
        UiThreadHelper.RunOnUiThread(() =>
        {
            if (_window.IsLoaded)
            {
                _window.Close();
            }
        });
    }
}
