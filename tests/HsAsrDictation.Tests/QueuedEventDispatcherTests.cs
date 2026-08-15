using HsAsrDictation.Hotkeys;
using HsAsrDictation.Logging;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class QueuedEventDispatcherTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"press-speak-dispatch-tests-{Guid.NewGuid():N}");
    private readonly LocalLogService _logger;

    public QueuedEventDispatcherTests() => _logger = new LocalLogService(_logDirectory);

    public void Dispose()
    {
        _logger.Dispose();
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    [Fact]
    public void Enqueue_ProcessesItemsInOrder()
    {
        const int itemCount = 200;
        var processed = new List<int>();
        using var allProcessed = new ManualResetEventSlim();
        using var dispatcher = new QueuedEventDispatcher<int>(item =>
        {
            lock (processed)
            {
                processed.Add(item);
                if (processed.Count == itemCount)
                {
                    allProcessed.Set();
                }
            }
        }, _logger);

        for (var i = 0; i < itemCount; i++)
        {
            dispatcher.Enqueue(i);
        }

        Assert.True(allProcessed.Wait(TimeSpan.FromSeconds(10)), "消费者未在超时前处理完所有事件。");
        Assert.Equal(Enumerable.Range(0, itemCount), processed);
    }

    [Fact]
    public void Enqueue_HandlerException_DoesNotStopConsumer()
    {
        var processed = new List<int>();
        using var allProcessed = new ManualResetEventSlim();
        using var dispatcher = new QueuedEventDispatcher<int>(item =>
        {
            if (item == 1)
            {
                throw new InvalidOperationException("模拟单个事件处理失败。");
            }

            lock (processed)
            {
                processed.Add(item);
                if (processed.Count == 2)
                {
                    allProcessed.Set();
                }
            }
        }, _logger);

        dispatcher.Enqueue(0);
        dispatcher.Enqueue(1);
        dispatcher.Enqueue(2);

        Assert.True(allProcessed.Wait(TimeSpan.FromSeconds(10)), "消费者被单个事件的异常终止。");
        Assert.Equal(new[] { 0, 2 }, processed);
    }

    [Fact]
    public void Dispose_DrainsQueuedItemsBeforeReturning()
    {
        var processedCount = 0;
        var dispatcher = new QueuedEventDispatcher<int>(
            _ => Interlocked.Increment(ref processedCount),
            _logger);

        for (var i = 0; i < 50; i++)
        {
            dispatcher.Enqueue(i);
        }

        dispatcher.Dispose();

        Assert.Equal(50, processedCount);
    }
}
