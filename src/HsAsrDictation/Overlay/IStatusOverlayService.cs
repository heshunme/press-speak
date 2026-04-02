namespace HsAsrDictation.Overlay;

public interface IStatusOverlayService : IDisposable
{
    void Show(string text);

    void Hide();
}
