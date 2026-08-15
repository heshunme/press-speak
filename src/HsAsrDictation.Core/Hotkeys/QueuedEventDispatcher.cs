using System.Threading.Channels;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

/// <summary>
/// 无界通道 + 后台单消费者的事件派发器：入队方（如低级键盘钩子回调）立即返回，
/// 事件按入队顺序在消费者线程逐个处理；单个事件处理抛异常只记日志，不会终止派发循环。
/// </summary>
internal sealed class QueuedEventDispatcher<T> : IDisposable
{
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<T> _channel;
    private readonly Action<T> _handler;
    private readonly LocalLogService _logger;
    private readonly Task _consumerTask;

    public QueuedEventDispatcher(Action<T> handler, LocalLogService logger)
    {
        _handler = handler;
        _logger = logger;
        _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(RunConsumerLoopAsync);
    }

    /// <summary>入队事件；通道无界，调用方永远不会被阻塞。</summary>
    public void Enqueue(T item) => _channel.Writer.TryWrite(item);

    /// <summary>停止接收新事件，并等待已入队事件全部处理完后退出消费者。</summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try
        {
            _consumerTask.Wait(DisposeWaitTimeout);
        }
        catch (Exception ex)
        {
            _logger.Error("事件派发循环退出异常。", ex);
        }
    }

    private async Task RunConsumerLoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                _handler(item);
            }
            catch (Exception ex)
            {
                _logger.Error("事件处理异常，派发循环继续。", ex);
            }
        }
    }
}
