namespace HsAsrDictation.Overlay;

public interface IStatusOverlayService : IDisposable
{
    void Show(string statusText, string? previewText);

    void Hide();
}
