using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Logging;
using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SegmentedDecodePipelineTests : IDisposable
{
    private const int SampleRate = SileroVadSegmenter.SampleRate;

    private readonly string _tempDirectory;
    private readonly LocalLogService _logger;

    public SegmentedDecodePipelineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"hs-asr-segmented-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _logger = new LocalLogService(Path.Combine(_tempDirectory, "logs"));
    }

    public void Dispose()
    {
        _logger.Dispose();

        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private static float[] Samples(int count, float value = 0.2f)
    {
        var samples = new float[count];
        Array.Fill(samples, value);
        return samples;
    }

    private static SegmentedDecodeOptions Options(
        double minBatchSeconds = 1.5,
        double paddingSeconds = 0.12) => new()
    {
        MinDecodeBatch = TimeSpan.FromSeconds(minBatchSeconds),
        BoundaryPadding = TimeSpan.FromSeconds(paddingSeconds)
    };

    private SegmentedDecodePipeline CreatePipeline(
        FakeSegmenter segmenter,
        FakeAsrEngine engine,
        SegmentedDecodeOptions? options = null,
        string separator = "",
        Action? onPreviewChanged = null) =>
        new(
            segmenter,
            engine,
            options ?? Options(),
            separator,
            onPreviewChanged ?? (() => { }),
            _logger);

    [Fact]
    public async Task CompleteAsync_ConcatenatesSegmentTextsInSpokenOrder()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["第一段", "第二段", "第三段"]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        // 每段都超过最小批阈值，因此逐段派发。
        for (var i = 0; i < 3; i++)
        {
            pipeline.AcceptChunk(Samples(SampleRate * 2));
        }

        var text = await pipeline.CompleteAsync();

        Assert.Equal("第一段第二段第三段", text);
        Assert.Equal(3, engine.CallCount);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AcceptChunk_MergesShortSegmentsUntilBatchThresholdIsReached()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["合并批"]);
        var pipeline = CreatePipeline(segmenter, engine, Options(minBatchSeconds: 1.5));
        pipeline.Start();

        // 三段各 0.6s：前两段不足 1.5s 不该派发，第三段累计 1.8s 才触发一次解码。
        for (var i = 0; i < 3; i++)
        {
            pipeline.AcceptChunk(Samples((int)(SampleRate * 0.6)));
        }

        var text = await pipeline.CompleteAsync();

        Assert.Equal("合并批", text);
        Assert.Equal(1, engine.CallCount);

        // 一次解码应拿到三段合并后的音频（含边界补齐），而不是单段。
        Assert.True(
            engine.ReceivedLengths[0] >= (int)(SampleRate * 1.8),
            $"实际收到 {engine.ReceivedLengths[0]} 样本，未合并三段。");
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task CompleteAsync_FlushesRemainderBelowBatchThreshold()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["零头"]);
        var pipeline = CreatePipeline(segmenter, engine, Options(minBatchSeconds: 5));
        pipeline.Start();

        pipeline.AcceptChunk(Samples((int)(SampleRate * 0.5)));

        var text = await pipeline.CompleteAsync();

        // 不足一批的余量必须在收尾时派发，否则尾句会丢。
        Assert.Equal("零头", text);
        Assert.Equal(1, engine.CallCount);
        Assert.True(segmenter.FlushCallCount >= 1);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RunDecodeLoop_IsolatesFailedBatch_AndKeepsOtherSegments()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["前段", null, "后段"]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        for (var i = 0; i < 3; i++)
        {
            pipeline.AcceptChunk(Samples(SampleRate * 2));
        }

        var text = await pipeline.CompleteAsync();

        // 中间那批抛异常，前后两段仍然写回。
        Assert.Equal("前段后段", text);
        Assert.False(pipeline.SegmentationFailed);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task RunSegmentLoop_MarksFailure_WhenSegmenterThrows()
    {
        var segmenter = new FakeSegmenter { ThrowOnAccept = true };
        var engine = new FakeAsrEngine(["不该出现"]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        pipeline.AcceptChunk(Samples(SampleRate));

        var text = await pipeline.CompleteAsync();

        Assert.True(pipeline.SegmentationFailed);
        Assert.Equal(string.Empty, text);
        Assert.Equal(0, engine.CallCount);
        Assert.False(pipeline.IsSpeechActive);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task PreviewText_AccumulatesDecodedSegments_AndNotifiesOnce_PerSegment()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["甲", "乙"]);
        var notifications = 0;
        var pipeline = CreatePipeline(
            segmenter,
            engine,
            separator: " ",
            onPreviewChanged: () => Interlocked.Increment(ref notifications));
        pipeline.Start();

        for (var i = 0; i < 2; i++)
        {
            pipeline.AcceptChunk(Samples(SampleRate * 2));
        }

        var text = await pipeline.CompleteAsync();

        Assert.Equal("甲 乙", text);
        Assert.Equal("甲 乙", pipeline.PreviewText);
        Assert.Equal(2, notifications);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task IsSpeechActive_TracksSegmenter_AndClearsAfterCompletion()
    {
        var segmenter = new FakeSegmenter { IsSpeechActive = true };
        var engine = new FakeAsrEngine(["讲话中"]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        pipeline.AcceptChunk(Samples(SampleRate));
        await segmenter.AcceptObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(pipeline.IsSpeechActive);

        await pipeline.CompleteAsync();

        // 收尾后必须归位，否则自适应尾录会误判为"还在说话"。
        Assert.False(pipeline.IsSpeechActive);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task HasPendingWork_IsFalse_AfterAllSegmentsDecoded()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine(["完毕"]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        pipeline.AcceptChunk(Samples(SampleRate * 2));

        await pipeline.CompleteAsync();

        Assert.False(pipeline.HasPendingWork);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesSegmenter_AndIsIdempotent()
    {
        var segmenter = new FakeSegmenter();
        var engine = new FakeAsrEngine([]);
        var pipeline = CreatePipeline(segmenter, engine);
        pipeline.Start();

        await pipeline.DisposeAsync();
        await pipeline.DisposeAsync();

        Assert.Equal(1, segmenter.DisposeCallCount);
    }

    /// <summary>
    /// 每次 AcceptChunk 把刚喂入的那一块整体当成一个语音段切出。
    /// 段偏移与流水线滚动缓冲里的真实数据严格对应，因此"每块音频 → 一个段"是确定的，
    /// 不依赖 channel 的读取时序（预置段队列会让多段在一次 drain 里被合并，测试就不可靠了）。
    /// </summary>
    private sealed class FakeSegmenter : IAudioSegmenter
    {
        private readonly Queue<AudioSegment> _queue = new();
        private readonly object _sync = new();
        private long _fedSamples;

        public bool ThrowOnAccept { get; init; }

        public bool IsSpeechActive { get; set; }

        public int FlushCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public TaskCompletionSource AcceptObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AcceptChunk(float[] samples)
        {
            if (ThrowOnAccept)
            {
                throw new InvalidOperationException("分段器故障。");
            }

            lock (_sync)
            {
                _queue.Enqueue(new AudioSegment(_fedSamples, samples));
                _fedSamples += samples.Length;
            }

            AcceptObserved.TrySetResult();
        }

        public bool TryDequeue(out AudioSegment segment)
        {
            lock (_sync)
            {
                if (_queue.Count == 0)
                {
                    segment = null!;
                    return false;
                }

                segment = _queue.Dequeue();
                return true;
            }
        }

        public void Flush()
        {
            FlushCallCount++;
            IsSpeechActive = false;
        }

        public void Dispose() => DisposeCallCount++;
    }

    /// <summary>按调用顺序返回预设文本；null 表示该批抛异常。</summary>
    private sealed class FakeAsrEngine : IAsrEngine
    {
        private readonly string?[] _responses;
        private int _index;

        public FakeAsrEngine(string?[] responses) => _responses = responses;

        public bool IsReady => true;

        public int CallCount { get; private set; }

        public List<int> ReceivedLengths { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false) =>
            Task.CompletedTask;

        public Task UnloadAsync() => Task.CompletedTask;

        public Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default)
        {
            CallCount++;
            ReceivedLengths.Add(pcm16kMono.Length);

            var response = _index < _responses.Length ? _responses[_index] : string.Empty;
            _index++;

            if (response is null)
            {
                throw new InvalidOperationException("解码故障。");
            }

            return Task.FromResult(new AsrResult
            {
                Success = true,
                Text = response,
                AudioDuration = TimeSpan.FromSeconds(pcm16kMono.Length / (double)SampleRate),
                DecodeLatency = TimeSpan.FromMilliseconds(10)
            });
        }

        public void Dispose()
        {
        }
    }
}
