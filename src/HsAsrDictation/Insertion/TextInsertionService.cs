using System.Runtime.InteropServices;
using System.Windows;
using HsAsrDictation.Foreground;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;
using HsAsrDictation.Settings;
using FormsKeys = System.Windows.Forms.Keys;

namespace HsAsrDictation.Insertion;

public sealed class TextInsertionService : ITextInsertionService
{
    private readonly SettingsService _settingsService;
    private readonly ForegroundContextService _foregroundContextService;
    private readonly LocalLogService _logger;

    public TextInsertionService(
        SettingsService settingsService,
        ForegroundContextService foregroundContextService,
        LocalLogService logger)
    {
        _settingsService = settingsService;
        _foregroundContextService = foregroundContextService;
        _logger = logger;
    }

    public async Task<InsertionResult> InsertAsync(string text, ForegroundContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new InsertionResult
            {
                Success = false,
                Error = "文本为空。"
            };
        }

        if (context.IsPasswordField)
        {
            return new InsertionResult
            {
                Success = false,
                Error = "检测到密码输入框，已拒绝注入。"
            };
        }

        _foregroundContextService.Restore(context);

        if (TrySendUnicode(text))
        {
            return new InsertionResult
            {
                Success = true,
                Method = "SendInput"
            };
        }

        if (!_settingsService.Current.AllowClipboardFallback)
        {
            return new InsertionResult
            {
                Success = false,
                Method = "SendInput",
                Error = "Unicode 注入失败，且未启用剪贴板回退。"
            };
        }

        return await PasteViaClipboardAsync(text, ct);
    }

    private bool TrySendUnicode(string text)
    {
        var inputs = new List<Win32.INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(Win32.CreateUnicodeKeyInput(character, keyUp: false));
            inputs.Add(Win32.CreateUnicodeKeyInput(character, keyUp: true));
        }

        var sent = Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32.INPUT>());
        var success = sent == inputs.Count;

        if (!success)
        {
            _logger.Warn($"SendInput 注入失败，返回值：{sent}");
        }

        return success;
    }

    private async Task<InsertionResult> PasteViaClipboardAsync(string text, CancellationToken ct)
    {
        System.Windows.IDataObject? snapshot = null;
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF Dispatcher 不可用。");

        await dispatcher.InvokeAsync(() =>
        {
            if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text) ||
                System.Windows.Clipboard.ContainsText())
            {
                snapshot = System.Windows.Clipboard.GetDataObject();
            }

            System.Windows.Clipboard.SetText(text);
        });

        if (!TrySendPasteShortcut())
        {
            return new InsertionResult
            {
                Success = false,
                Method = "Clipboard",
                Error = "Ctrl+V 发送失败。"
            };
        }

        await Task.Delay(150, ct);

        await dispatcher.InvokeAsync(() =>
        {
            if (snapshot is not null)
            {
                System.Windows.Clipboard.SetDataObject(snapshot, true);
            }
        });

        return new InsertionResult
        {
            Success = true,
            Method = "Clipboard"
        };
    }

    private bool TrySendPasteShortcut()
    {
        var inputs = new[]
        {
            Win32.CreateVirtualKeyInput((ushort)FormsKeys.ControlKey, keyUp: false),
            Win32.CreateVirtualKeyInput((ushort)FormsKeys.V, keyUp: false),
            Win32.CreateVirtualKeyInput((ushort)FormsKeys.V, keyUp: true),
            Win32.CreateVirtualKeyInput((ushort)FormsKeys.ControlKey, keyUp: true)
        };

        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        return sent == inputs.Length;
    }
}
