using System.Threading.Channels;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Services;

/// <summary>
/// 分段解码流水线：录音期间把音频按语音停顿切段并陆续解码，使松开热键后只需等待最后一段，
/// 而不是整段音频（见 docs/vad-segmented-decoding-design.md）。
///
/// 线程模型：<see cref="AcceptChunk"/> 由录音回调线程调用，只做入队；VAD 循环与解码 worker
/// 各占一个后台任务——前者独占 <see cref="IAudioSegmenter"/>（满足其"串行调用"约定），
/// 后者独占离线引擎调用。两者用 <see cref="_batches"/> 串联，保证解码顺序即说话顺序。
/// </summary>
internal sealed class SegmentedDecodePipeline : IAsyncDisposable
{
    private readonly IAudioSegmenter _segmenter;
    private readonly IAsrEngine _asrEngine;
    private readonly string _segmentSeparator;
    private readonly Action _onPreviewChanged;
    private readonly LocalLogService _logger;

    private readonly int _minDecodeBatchSamples;
    private readonly int _boundaryPaddingSamples;

    private readonly Channel<float[]> _chunks = Channel.CreateUnbounded<float[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly Channel<DecodeBatch> _batches = Channel.CreateUnbounded<DecodeBatch>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly RollingSampleBuffer _rawBuffer = new();
    private readonly List<AudioSegment> _pendingSegments = [];
    private readonly List<string> _decodedTexts = [];
    private readonly object _textSync = new();

    private Task? _segmentLoopTask;
    private Task? _decodeLoopTask;
    private long _pendingSpeechSamples;
    private int _outstandingSegments;
    private long _acceptedChunks;
    private long _processedChunks;
    private volatile bool _speechActive;
    private volatile bool _failed;
    private volatile string _previewText = string.Empty;
    private bool _disposed;

    public SegmentedDecodePipeline(
        IAudioSegmenter segmenter,
        IAsrEngine asrEngine,
        SegmentedDecodeOptions options,
        string segmentSeparator,
        Action onPreviewChanged,
        LocalLogService logger)
    {
        _segmenter = segmenter;
        _asrEngine = asrEngine;
        _segmentSeparator = segmentSeparator;
        _onPreviewChanged = onPreviewChanged;
        _logger = logger;
        _minDecodeBatchSamples =
            (int)(options.MinDecodeBatch.TotalSeconds * SileroVadSegmenter.SampleRate);
        _boundaryPaddingSamples =
            (int)(options.BoundaryPadding.TotalSeconds * SileroVadSegmenter.SampleRate);
    }

    /// <summary>此刻是否正在说话；松键时用于判断能否跳过尾录。</summary>
    public bool IsSpeechActive => _speechActive;

    /// <summary>
    /// 是否还有未完成的工作：尚未被 VAD 看过的音频，或已切出但未解码完的段。
    /// 必须把"未消费的音频"算进来——否则刚喂进去还没轮到处理时，
    /// 会被误判为"已经没活了"，自适应尾录就会提前砍掉还没识别的语音。
    /// </summary>
    public bool HasPendingWork =>
        Volatile.Read(ref _outstandingSegments) > 0 ||
        Volatile.Read(ref _processedChunks) < Volatile.Read(ref _acceptedChunks);

    /// <summary>VAD 循环整体失败；调用方据此回落到整段解码。</summary>
    public bool SegmentationFailed => _failed;

    /// <summary>已解码段落拼成的累积预览。</summary>
    public string PreviewText => _previewText;

    public void Start()
    {
        _segmentLoopTask = Task.Run(RunSegmentLoopAsync);
        _decodeLoopTask = Task.Run(RunDecodeLoopAsync);
    }

    public void AcceptChunk(float[] samples)
    {
        // 先登记再入队：HasPendingWork 宁可短暂多报，也不能漏报。
        Interlocked.Increment(ref _acceptedChunks);

        if (!_chunks.Writer.TryWrite(samples))
        {
            Interlocked.Increment(ref _processedChunks);
        }
    }

    /// <summary>
    /// 收尾：封口最后一段、等待队列排空，返回拼接后的全部文本。幂等。
    /// </summary>
    public async Task<string> CompleteAsync()
    {
        _chunks.Writer.TryComplete();

        if (_segmentLoopTask is not null)
        {
            await _segmentLoopTask;
        }

        if (_decodeLoopTask is not null)
        {
            await _decodeLoopTask;
        }

        return GetAccumulatedText();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 先让两个循环退出，再释放分段器：绝不能在 VAD 循环仍在使用原生对象时释放它。
        try
        {
            await CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("结束分段解码流水线失败。", ex);
        }

        _segmenter.Dispose();
    }

    private string GetAccumulatedText()
    {
        lock (_textSync)
        {
            return string.Join(_segmentSeparator, _decodedTexts);
        }
    }

    private async Task RunSegmentLoopAsync()
    {
        try
        {
            await foreach (var chunk in _chunks.Reader.ReadAllAsync())
            {
                try
                {
                    _rawBuffer.Append(chunk);
                    _segmenter.AcceptChunk(chunk);
                    _speechActive = _segmenter.IsSpeechActive;

                    CollectSegments();
                    TryDispatchBatch(force: false);
                }
                finally
                {
                    // 无论成功与否都记为已消费，避免失败时 HasPendingWork 永久为真。
                    Interlocked.Increment(ref _processedChunks);
                }
            }

            // 音频结束：封口仍在进行中的段，并把不足一批的余量也派发掉。
            _segmenter.Flush();
            _speechActive = false;
            CollectSegments();
            TryDispatchBatch(force: true);
        }
        catch (Exception ex)
        {
            _failed = true;
            _speechActive = false;
            _logger.Error("分段解码的 VAD 循环失败，本次听写将回落整段解码。", ex);
        }
        finally
        {
            _batches.Writer.TryComplete();

            // 循环已退出，剩下的音频不会再有人消费：把计数追平，
            // 否则 HasPendingWork 会永久为真，自适应尾录退化成一直等到上限。
            DrainUnprocessedChunkCount();
        }
    }

    private void DrainUnprocessedChunkCount()
    {
        // 读取顺序：先 accepted 再 processed，保证不会算出负的待处理量。
        var accepted = Volatile.Read(ref _acceptedChunks);
        var processed = Volatile.Read(ref _processedChunks);
        if (accepted > processed)
        {
            Interlocked.Add(ref _processedChunks, accepted - processed);
        }
    }

    private void CollectSegments()
    {
        while (_segmenter.TryDequeue(out var segment))
        {
            _pendingSegments.Add(segment);
            _pendingSpeechSamples += segment.Samples.Length;

            // 在"切出"时就计入未完成量，使松键判断不会漏掉尚未派发的段。
            Interlocked.Increment(ref _outstandingSegments);
        }
    }

    private void TryDispatchBatch(bool force)
    {
        if (_pendingSegments.Count == 0)
        {
            return;
        }

        if (!force && _pendingSpeechSamples < _minDecodeBatchSamples)
        {
            return;
        }

        var samples = BuildPaddedBatchSamples();
        var segmentCount = _pendingSegments.Count;
        var lastEnd = _pendingSegments[^1].EndSampleExclusive;

        _pendingSegments.Clear();
        _pendingSpeechSamples = 0;

        if (samples.Length == 0)
        {
            Interlocked.Add(ref _outstandingSegments, -segmentCount);
            return;
        }

        if (!_batches.Writer.TryWrite(new DecodeBatch(samples, segmentCount)))
        {
            Interlocked.Add(ref _outstandingSegments, -segmentCount);
            return;
        }

        // 只保留补齐所需的尾巴：强制切分时下一段紧接着当前段结束位置开始。
        _rawBuffer.TrimBefore(lastEnd - _boundaryPaddingSamples);
    }

    /// <summary>
    /// 把本批各段按边界补齐后首尾相接。段间的停顿不并入（否则等于把思考停顿又送进解码），
    /// 拼接点落在补齐出来的低能量区，听起来是连续的。
    /// </summary>
    private float[] BuildPaddedBatchSamples()
    {
        var parts = new List<float[]>(_pendingSegments.Count);
        var total = 0;

        foreach (var segment in _pendingSegments)
        {
            var part = _rawBuffer.Extract(
                segment.StartSample - _boundaryPaddingSamples,
                segment.EndSampleExclusive + _boundaryPaddingSamples);

            if (part.Length == 0)
            {
                // 补齐窗口落在已裁掉的区域时退回原始样本，绝不丢段。
                part = segment.Samples;
            }

            parts.Add(part);
            total += part.Length;
        }

        var merged = new float[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, merged, offset, part.Length);
            offset += part.Length;
        }

        return merged;
    }

    private async Task RunDecodeLoopAsync()
    {
        await foreach (var batch in _batches.Reader.ReadAllAsync())
        {
            try
            {
                var result = await _asrEngine.TranscribeAsync(batch.Samples);
                LogBatchLatency(result);

                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    AppendText(DictationTextNormalizer.Normalize(result.Text));
                }
                else
                {
                    // 单批失败只影响这一段，其余段落照常拼接写回。
                    _logger.Warn($"分段解码未返回文本，已跳过该批：{result.Error ?? "无错误信息"}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("分段解码失败，已跳过该批。", ex);
            }
            finally
            {
                Interlocked.Add(ref _outstandingSegments, -batch.SegmentCount);
            }
        }
    }

    private void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string preview;
        lock (_textSync)
        {
            _decodedTexts.Add(text);
            preview = string.Join(_segmentSeparator, _decodedTexts);
        }

        _previewText = preview;
        _onPreviewChanged();
    }

    private void LogBatchLatency(AsrResult result)
    {
        var audioSeconds = result.AudioDuration.TotalSeconds;
        var decodeSeconds = result.DecodeLatency.TotalSeconds;
        var rtf = audioSeconds > 0 ? decodeSeconds / audioSeconds : 0d;

        _logger.Info(
            $"离线解码耗时（分段）：音频 {audioSeconds:0.00} s，解码 {decodeSeconds * 1000:0} ms，" +
            $"RTF {rtf:0.00}，文本 {result.Text.Length} 字。");
    }

    private sealed record DecodeBatch(float[] Samples, int SegmentCount);

    /// <summary>
    /// 保留最近音频的滚动缓冲，供段边界补齐使用。派发后裁掉已消费部分，
    /// 常驻量只有"补齐窗口 + 当前未封口段"，不会把整段录音再存一份。
    /// </summary>
    private sealed class RollingSampleBuffer
    {
        private readonly List<float> _samples = [];
        private long _startSample;

        public void Append(float[] chunk) => _samples.AddRange(chunk);

        public float[] Extract(long fromSample, long toSampleExclusive)
        {
            var from = (int)Math.Max(0, fromSample - _startSample);
            var to = (int)Math.Min(_samples.Count, toSampleExclusive - _startSample);

            if (to <= from)
            {
                return [];
            }

            var extracted = new float[to - from];
            _samples.CopyTo(from, extracted, 0, extracted.Length);
            return extracted;
        }

        public void TrimBefore(long sample)
        {
            var count = (int)Math.Min(_samples.Count, sample - _startSample);
            if (count <= 0)
            {
                return;
            }

            _samples.RemoveRange(0, count);
            _startSample += count;
        }
    }
}
