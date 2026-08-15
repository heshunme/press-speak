namespace HsAsrDictation.Asr;

/// <summary>
/// 引用计数资源容器：跟踪活动租约（如流式会话对识别器的裸引用）。
/// 停用（Retire）后不再发放新租约；等待存量租约归零（带超时）后由等待方释放资源，
/// 若超时仍有活动租约，则由最后一次 <see cref="ReleaseLease"/> 兜底释放，
/// 保证任何情况下都不会释放仍在使用中的资源，也不会泄漏。
/// </summary>
public sealed class RefCountedResource<TResource>
    where TResource : class, IDisposable
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeLeaseCount;
    private bool _retired;
    private bool _disposed;

    public RefCountedResource(TResource resource)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    public TResource Resource { get; }

    public int ActiveLeaseCount
    {
        get
        {
            lock (_sync)
            {
                return _activeLeaseCount;
            }
        }
    }

    /// <summary>发放一个租约；每次 Acquire 必须配对一次 <see cref="ReleaseLease"/>。</summary>
    public void AcquireLease()
    {
        lock (_sync)
        {
            if (_retired)
            {
                throw new InvalidOperationException("资源已停用，不再发放新租约。");
            }

            _activeLeaseCount++;
        }
    }

    /// <summary>
    /// 归还租约；若资源已停用且这是最后一个租约，由本调用兜底释放资源。
    /// 多余的 Release（未配对 Acquire）直接忽略，避免计数被压成负数导致提前放行等待方。
    /// </summary>
    public void ReleaseLease()
    {
        var disposeResource = false;
        lock (_sync)
        {
            if (_activeLeaseCount == 0)
            {
                return;
            }

            _activeLeaseCount--;
            if (_activeLeaseCount == 0)
            {
                _drained.TrySetResult();
                if (_retired && !_disposed)
                {
                    _disposed = true;
                    disposeResource = true;
                }
            }
        }

        if (disposeResource)
        {
            Resource.Dispose();
        }
    }

    /// <summary>
    /// 停止发放新租约，并等待存量租约归零；返回 true 表示已无活动租约（调用方可安全
    /// <see cref="TryDispose"/>），false 表示超时仍有活动租约（资源将由最后一次 ReleaseLease 兜底释放）。
    /// </summary>
    public async Task<bool> RetireAndWaitForDrainAsync(TimeSpan timeout)
    {
        Task drainTask;
        lock (_sync)
        {
            _retired = true;
            drainTask = _activeLeaseCount == 0 ? Task.CompletedTask : _drained.Task;
        }

        if (drainTask.IsCompleted)
        {
            return true;
        }

        return await Task.WhenAny(drainTask, Task.Delay(timeout)).ConfigureAwait(false) == drainTask;
    }

    /// <summary>
    /// 租约归零后由等待方调用以释放资源；返回 false 表示仍有活动租约
    ///（由最后一次 <see cref="ReleaseLease"/> 兜底释放）或资源已释放。幂等。
    /// </summary>
    public bool TryDispose()
    {
        var disposeResource = false;
        lock (_sync)
        {
            if (_activeLeaseCount == 0 && !_disposed)
            {
                _disposed = true;
                disposeResource = true;
            }
        }

        if (disposeResource)
        {
            Resource.Dispose();
        }

        return disposeResource;
    }
}
