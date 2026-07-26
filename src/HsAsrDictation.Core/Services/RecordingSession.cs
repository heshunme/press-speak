using System.Threading.Channels;
using HsAsrDictation.Asr;
using HsAsrDictation.Foreground;
using HsAsrDictation.Logging;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

/// <summary>
/// 一次录音会话的全部可变状态：设置快照、捕获上下文、流式识别管线与结束控制。
/// 会话对象随热键按下创建，结束处理完成后整体丢弃，避免跨会话共享可变字段。
/// </summary>
internal sealed class RecordingSession : IDisposable
{
    private readonly TaskCompletionSource _startCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Channel<float[]>? _streamingChannel;
    private Task? _streamingLoopTask;
    private IStreamingAsrSession? _streamingSession;
    private volatile string _previewText = string.Empty;
    private string _streamingFinalText = string.Empty;
    private volatile bool _streamingFailed;

    public RecordingSession(AppSettings settings, TimeSpan hotkeyReleaseTailDuration)
    {
        Settings = settings;
        HotkeyReleaseTailDuration = hotkeyReleaseTailDuration;
    }

    /// <summary>会话开始时的设置快照；会话期间保存新设置不影响本次听写。</summary>
    public AppSettings Settings { get; }

    public TimeSpan HotkeyReleaseTailDuration { get; }

    public ForegroundContext? CaptureContext { get; private set; }

    /// <summary>启动 IO（前台捕获、流式初始化、音频启动）完成或失败后置位；结束流程先等它，消除"释放先于启动完成"的窗口。</summary>
    public Task StartCompletion => _startCompletion.Task;

    /// <summary>仅在 DictationCoordinator 的状态锁内读写。</summary>
    public CancellationTokenSource? PendingTailFinalize;

    /// <summary>仅在 DictationCoordinator 的状态锁内读写。</summary>
    public bool FinalizationInProgress;

    public string PreviewText => _previewText;

    public bool StreamingFailed => _streamingFailed;

    public void SetCaptureContext(ForegroundContext context) => CaptureContext = context;

    public void MarkStartSucceeded() => _startCompletion.TrySetResult();

    public void MarkStartFailed(Exception exception) => _startCompletion.TrySetException(exception);

    public void MarkStreamingFailed() => _streamingFailed = true;

    public async Task InitializeStreamingAsync(
        IStreamingAsrEngine engine,
        Action onPreviewChanged,
        LocalLogService logger)
    {
        await engine.InitializeAsync();
        _streamingSession = engine.CreateSession();
        _streamingChannel = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _streamingLoopTask = Task.Run(() => RunStreamingLoopAsync(onPreviewChanged, logger));
    }

    public void AcceptChunk(float[] samples) => _streamingChannel?.Writer.TryWrite(samples);

    /// <summary>结束流式管线并返回归一化后的最终流式文本；未启用流式时返回空串。</summary>
    public async Task<string> CompleteStreamingAsync()
    {
        if (_streamingChannel is null)
        {
            return string.Empty;
        }

        _streamingChannel.Writer.TryComplete();
        if (_streamingLoopTask is not null)
        {
            await _streamingLoopTask;
        }

        return _streamingFinalText;
    }

    public void Dispose()
    {
        _streamingSession?.Dispose();
        _streamingSession = null;
    }

    private async Task RunStreamingLoopAsync(Action onPreviewChanged, LocalLogService logger)
    {
        if (_streamingChannel is null || _streamingSession is null)
        {
            return;
        }

        try
        {
            await foreach (var chunk in _streamingChannel.Reader.ReadAllAsync())
            {
                await _streamingSession.AcceptAudioAsync(chunk);
                UpdatePreview(_streamingSession.GetCurrentResult(), onPreviewChanged);
            }

            var completed = await _streamingSession.CompleteAsync();
            UpdatePreview(completed, onPreviewChanged);
            _streamingFinalText = DictationTextNormalizer.Normalize(completed.Text);
        }
        catch (Exception ex)
        {
            _streamingFailed = true;
            logger.Error("流式识别执行失败。", ex);
        }
    }

    private void UpdatePreview(StreamingAsrResult result, Action onPreviewChanged)
    {
        if (!Settings.EnableStreamingPreview)
        {
            return;
        }

        var preview = DictationTextNormalizer.Normalize(result.Text);
        if (string.Equals(preview, _previewText, StringComparison.Ordinal))
        {
            return;
        }

        _previewText = preview;
        onPreviewChanged();
    }
}
