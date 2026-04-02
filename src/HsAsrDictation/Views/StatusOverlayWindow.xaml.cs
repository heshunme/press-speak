using System.Windows;

namespace HsAsrDictation.Views;

public partial class StatusOverlayWindow : Window
{
    public StatusOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetMessage(string message)
    {
        MessageTextBlock.Text = message;
    }

    public void UpdatePosition()
    {
        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 48;
    }
}
