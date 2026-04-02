using System.Text;
using System.Windows.Automation;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Foreground;

public sealed class ForegroundContextService
{
    private readonly LocalLogService _logger;

    public ForegroundContextService(LocalLogService logger)
    {
        _logger = logger;
    }

    public ForegroundContext Capture()
    {
        var handle = Win32.GetForegroundWindow();
        var title = GetWindowTitle(handle);
        var className = GetClassName(handle);
        var isPasswordField = false;

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is not null)
            {
                var currentValue = element.GetCurrentPropertyValue(
                    AutomationElement.IsPasswordProperty,
                    true);

                if (currentValue is bool passwordFlag)
                {
                    isPasswordField = passwordFlag;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"读取焦点控件失败：{ex.Message}");
        }

        return new ForegroundContext
        {
            WindowHandle = handle,
            WindowTitle = title,
            ClassName = className,
            IsPasswordField = isPasswordField
        };
    }

    public bool Restore(ForegroundContext context)
    {
        if (context.WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        return Win32.SetForegroundWindow(context.WindowHandle);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(512);
        Win32.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(256);
        Win32.GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }
}
