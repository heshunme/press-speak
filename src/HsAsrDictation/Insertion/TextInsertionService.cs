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
    private const int ClipboardOpenFailureHResult = unchecked((int)0x800401D0);
    private const int ClipboardAccessRetryCount = 5;
    private static readonly TimeSpan ClipboardAccessRetryDelay = TimeSpan.FromMilliseconds(80);

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

        var restored = _foregroundContextService.Restore(context);
        if (!restored)
        {
            _logger.Warn("恢复前台窗口失败，将继续尝试注入。");
        }

        await Task.Delay(50, ct);

        var preferClipboardInsertion = InputTargetClassifier.ShouldPreferClipboardInsertion(context);
        if (preferClipboardInsertion && _settingsService.Current.AllowClipboardFallback)
        {
            _logger.Info(
                $"目标窗口疑似终端，优先使用剪贴板回退。 process={context.ProcessName}, class={context.ClassName}, title={context.WindowTitle}");
            return await PasteViaClipboardAsync(text, ct);
        }

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
                Error = preferClipboardInsertion
                    ? "目标窗口疑似终端，Unicode 注入失败，且未启用剪贴板回退。"
                    : "Unicode 注入失败，且未启用剪贴板回退。"
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
            var error = Marshal.GetLastWin32Error();
            _logger.Warn($"SendInput 注入失败，返回值：{sent}，LastError={error}");
        }

        return success;
    }

    private async Task<InsertionResult> PasteViaClipboardAsync(string text, CancellationToken ct)
    {
        System.Windows.IDataObject? snapshot = null;
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF Dispatcher 不可用。");

        await RetryClipboardAccessAsync(async () =>
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Text) ||
                    System.Windows.Clipboard.ContainsText())
                {
                    snapshot = System.Windows.Clipboard.GetDataObject();
                }

                System.Windows.Clipboard.SetText(text);
            });
        }, ct);

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

        try
        {
            await RetryClipboardAccessAsync(async () =>
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (snapshot is not null)
                    {
                        System.Windows.Clipboard.SetDataObject(snapshot, copy: false);
                    }
                });
            }, ct);
        }
        catch (COMException ex) when (IsClipboardBusy(ex))
        {
            _logger.Warn($"恢复剪贴板快照失败，将保留当前剪贴板内容。 HRESULT=0x{ex.HResult:X8}");
        }

        return new InsertionResult
        {
            Success = true,
            Method = "Clipboard"
        };
    }

    private async Task RetryClipboardAccessAsync(Func<Task> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await operation();
                return;
            }
            catch (COMException ex) when (IsClipboardBusy(ex) && attempt < ClipboardAccessRetryCount)
            {
                _logger.Warn(
                    $"剪贴板暂时不可用，将重试。 attempt={attempt}/{ClipboardAccessRetryCount}, HRESULT=0x{ex.HResult:X8}");
                await Task.Delay(ClipboardAccessRetryDelay, ct);
            }
        }
    }

    private static bool IsClipboardBusy(COMException ex) => ex.HResult == ClipboardOpenFailureHResult;

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
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Warn($"Ctrl+V 注入失败，返回值：{sent}，LastError={error}");
            return false;
        }

        return true;
    }
}
