using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using HsAsrDictation.Interop;
using HsAsrDictation.Overlay;

namespace HsAsrDictation.Views;

public partial class StatusOverlayWindow : Window
{
    private const double BottomMarginDip = 48;

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

        var currentScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (currentScale <= 0)
        {
            currentScale = 1.0;
        }

        if (TryGetTargetWorkArea(out var workArea, out var targetScale))
        {
            // PerMonitorV2 下 Left/Top 是 DIP：目标屏上的物理尺寸 = DIP × 目标屏 scale；
            // 赋值时除以窗口当前 scale，使 WPF 换算回的物理落点精确等于目标值。
            var (left, top) = OverlayPlacement.ComputeBottomCenter(
                workArea.Left,
                workArea.Top,
                workArea.Right - workArea.Left,
                workArea.Bottom - workArea.Top,
                ActualWidth * targetScale,
                ActualHeight * targetScale,
                BottomMarginDip * targetScale);

            Left = left / currentScale;
            Top = top / currentScale;
            return;
        }

        var primaryWorkArea = SystemParameters.WorkArea;
        Left = primaryWorkArea.Left + (primaryWorkArea.Width - ActualWidth) / 2;
        Top = primaryWorkArea.Bottom - ActualHeight - BottomMarginDip;
    }

    // 目标屏 = 前台（焦点）窗口所在显示器；取不到前台窗口时退到光标所在屏，再失败退回主屏。
    private static bool TryGetTargetWorkArea(out Win32.RECT workArea, out double scale)
    {
        workArea = default;
        scale = 1.0;

        var monitor = IntPtr.Zero;
        var foreground = Win32.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            monitor = Win32.MonitorFromWindow(foreground, Win32.MONITOR_DEFAULTTONEAREST);
        }

        if (monitor == IntPtr.Zero && Win32.GetCursorPos(out var cursorPos))
        {
            monitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
        }

        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        if (Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        workArea = info.rcWork;
        return true;
    }
}
