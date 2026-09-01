using System.Threading.Channels;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Logging;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

/// <summary>
/// 一次录音会话的全部可变状态：设置快照、捕获上下文、流式识别管线、分段解码管线与结束控制。
/// 会话对象随热键按下创建，结束处理完成后整体丢弃，避免跨会话共享可变字段。
/// </summary>
internal sealed class RecordingSession : IAsyncDisposable
{
    private readonly TaskCompletionSource _startCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Channel<float[]>? _streamingChannel;
    private Task? _streamingLoopTask;
    private IStreamingAsrSession? _streamingSession;
    private SegmentedDecodePipeline? _segmentedPipeline;
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

    /// <summary>
    /// 录音期间的浮窗预览文本。分段解码启用时是已解码段落的累积文本，
    /// 否则是流式预览假名；两者不会同时存在（分段只在非流式模式启用）。
    /// </summary>
    public string PreviewText => _segmentedPipeline?.PreviewText ?? _previewText;

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

    /// <summary>分段解码是否已启用（模型可用且设置开启）。</summary>
    public bool SegmentedDecodeEnabled => _segmentedPipeline is not null;

    /// <summary>分段管线失败，需要回落整段解码。</summary>
    public bool SegmentedDecodeFailed => _segmentedPipeline?.SegmentationFailed == true;

    /// <summary>
    /// 松键时是否可以跳过尾录：不在说话且没有待解码的段，说明用户是在自然停顿后松手。
    /// 未启用分段解码时返回 false，保持既有的固定尾录行为。
    /// </summary>
    public bool CanSkipHotkeyReleaseTail =>
        _segmentedPipeline is { SegmentationFailed: false } pipeline &&
        !pipeline.IsSpeechActive &&
        !pipeline.HasPendingWork;

    /// <summary>
    /// 初始化分段解码管线。返回 false 表示分段不可用（模型缺失/构造失败），调用方走整段解码。
    /// </summary>
    public bool TryInitializeSegmentedDecode(
        IAudioSegmenterFactory segmenterFactory,
        IAsrEngine asrEngine,
        SegmentedDecodeOptions options,
        Action onPreviewChanged,
        LocalLogService logger)
    {
        var bufferCapacity = TimeSpan.FromSeconds(Settings.MaxRecordingDurationSeconds);
        var segmenter = segmenterFactory.TryCreate(bufferCapacity);
        if (segmenter is null)
        {
            return false;
        }

        // 标点会自己断句，所以段间不加分隔符；关闭标点时补空格，否则相邻段会糊在一起。
        var separator = Settings.EnablePunctuation ? string.Empty : " ";

        var pipeline = new SegmentedDecodePipeline(
            segmenter,
            asrEngine,
            options,
            separator,
            onPreviewChanged,
            logger);

        pipeline.Start();
        _segmentedPipeline = pipeline;
        return true;
    }

    /// <summary>结束分段解码并返回拼接后的文本；未启用时返回空串。</summary>
    public async Task<string> CompleteSegmentedDecodeAsync()
    {
        if (_segmentedPipeline is not { } pipeline)
        {
            return string.Empty;
        }

        return await pipeline.CompleteAsync();
    }

    public void AcceptChunk(float[] samples)
    {
        _streamingChannel?.Writer.TryWrite(samples);
        _segmentedPipeline?.AcceptChunk(samples);
    }

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

    /// <summary>
    /// 释放会话持有的原生资源。分段管线的两个循环任务必须先退出才能释放原生 VAD，
    /// 因此这里是异步的（<see cref="SegmentedDecodePipeline.DisposeAsync"/> 内部会先等循环结束）。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_segmentedPipeline is { } pipeline)
        {
            _segmentedPipeline = null;
            await pipeline.DisposeAsync();
        }

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
