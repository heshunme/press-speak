using HsAsrDictation.Views;

namespace HsAsrDictation.Overlay;

public sealed class StatusOverlayService : IStatusOverlayService
{
    private readonly StatusOverlayWindow _window;

    public StatusOverlayService()
    {
        _window = new StatusOverlayWindow();
    }

    public void Show(string text)
    {
        RunOnUiThread(() =>
        {
            _window.SetMessage(text);

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _window.UpdatePosition();
        });
    }

    public void Hide()
    {
        RunOnUiThread(() =>
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }
        });
    }

    public void Dispose()
    {
        RunOnUiThread(() =>
        {
            if (_window.IsLoaded)
            {
                _window.Close();
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
