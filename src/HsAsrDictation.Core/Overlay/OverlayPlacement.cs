namespace HsAsrDictation.Overlay;

/// <summary>
/// 悬浮窗定位的纯计算部分。所有坐标与尺寸必须使用同一单位（调用方约定为物理像素），
/// 输出与输入同单位；DPI 换算由调用方负责。
/// </summary>
public static class OverlayPlacement
{
    /// <summary>
    /// 在工作区内水平居中、贴底 margin 定位；窗口比工作区大时钳制到工作区边缘（贴左/贴顶）。
    /// </summary>
    public static (double Left, double Top) ComputeBottomCenter(
        double workLeft, double workTop, double workWidth, double workHeight,
        double windowWidth, double windowHeight, double bottomMargin)
    {
        var maxLeft = workLeft + workWidth - windowWidth;
        var maxTop = workTop + workHeight - windowHeight;

        var left = Clamp(workLeft + (workWidth - windowWidth) / 2, workLeft, maxLeft);
        var top = Clamp(maxTop - bottomMargin, workTop, maxTop);

        return (left, top);
    }

    private static double Clamp(double value, double min, double max)
    {
        // 窗口超出工作区时 max < min，此时贴 min（左/顶）边。
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(value, max));
    }
}
