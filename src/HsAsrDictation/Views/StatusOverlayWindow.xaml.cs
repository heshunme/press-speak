using System.Windows;

namespace HsAsrDictation.Views;

public partial class StatusOverlayWindow : Window
{
    public StatusOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetMessage(string statusText, string? previewText)
    {
        StatusTextBlock.Text = statusText;
        PreviewTextBlock.Text = previewText ?? string.Empty;
        PreviewTextBlock.Visibility = string.IsNullOrWhiteSpace(previewText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void UpdatePosition()
    {
        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 48;
    }
}
