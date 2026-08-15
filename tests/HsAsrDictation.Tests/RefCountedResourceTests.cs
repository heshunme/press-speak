using HsAsrDictation.Asr;
using Xunit;

namespace HsAsrDictation.Tests;

/// <summary>
/// 流式引擎卸载竞态修复的核心可测单元：识别器的引用计数容器。
/// 保证"停用后等租约归零再释放、超时由最后一次 Release 兜底释放、绝不释放使用中的资源"。
/// </summary>
public sealed class RefCountedResourceTests
{
    [Fact]
    public void AcquireAndReleaseLease_TracksActiveCount()
    {
        var resource = new RefCountedResource<FakeDisposable>(new FakeDisposable());

        resource.AcquireLease();
        resource.AcquireLease();
        Assert.Equal(2, resource.ActiveLeaseCount);

        resource.ReleaseLease();
        Assert.Equal(1, resource.ActiveLeaseCount);

        resource.ReleaseLease();
        Assert.Equal(0, resource.ActiveLeaseCount);
    }

    [Fact]
    public void ReleaseLease_WithoutMatchingAcquire_IsIgnored()
    {
        var resource = new RefCountedResource<FakeDisposable>(new FakeDisposable());

        resource.AcquireLease();
        resource.ReleaseLease();
        resource.ReleaseLease();

        Assert.Equal(0, resource.ActiveLeaseCount);
    }

    [Fact]
    public void AcquireLease_AfterRetire_Throws()
    {
        var resource = new RefCountedResource<FakeDisposable>(new FakeDisposable());
        resource.AcquireLease();
        resource.ReleaseLease();

        var retireTask = resource.RetireAndWaitForDrainAsync(TimeSpan.FromSeconds(1));
        Assert.True(retireTask.IsCompletedSuccessfully);

        Assert.Throws<InvalidOperationException>(() => resource.AcquireLease());
    }

    [Fact]
    public async Task RetireAndWaitForDrainAsync_WithActiveLease_CompletesAfterLastRelease()
    {
        var resource = new RefCountedResource<FakeDisposable>(new FakeDisposable());
        resource.AcquireLease();

        var drainTask = resource.RetireAndWaitForDrainAsync(TimeSpan.FromSeconds(5));
        Assert.False(drainTask.IsCompleted);

        resource.ReleaseLease();

        Assert.True(await drainTask);
        // 已停用资源的最后一个租约释放时，由 ReleaseLease 兜底释放。
        Assert.Equal(1, resource.Resource.DisposeCalls);
    }

    [Fact]
    public async Task RetireAndWaitForDrainAsync_TimesOut_ReturnsFalse_AndLastReleaseDisposesResource()
    {
        var disposable = new FakeDisposable();
        var resource = new RefCountedResource<FakeDisposable>(disposable);
        resource.AcquireLease();

        // 超时仍有活动租约：返回 false 且不释放资源（不能 dispose 使用中的识别器）。
        Assert.False(await resource.RetireAndWaitForDrainAsync(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(0, disposable.DisposeCalls);

        // 兜底：最后一个租约释放时由 ReleaseLease 负责释放，避免泄漏。
        resource.ReleaseLease();
        Assert.Equal(1, disposable.DisposeCalls);
    }

    [Fact]
    public void TryDispose_WithActiveLease_DoesNotDispose()
    {
        var disposable = new FakeDisposable();
        var resource = new RefCountedResource<FakeDisposable>(disposable);
        resource.AcquireLease();

        Assert.False(resource.TryDispose());
        Assert.Equal(0, disposable.DisposeCalls);
    }

    [Fact]
    public void TryDispose_WhenDrained_DisposesExactlyOnce()
    {
        var disposable = new FakeDisposable();
        var resource = new RefCountedResource<FakeDisposable>(disposable);

        Assert.True(resource.TryDispose());
        Assert.False(resource.TryDispose());
        Assert.Equal(1, disposable.DisposeCalls);
    }

    [Fact]
    public async Task DrainedResource_DisposedOnce_WhetherByWaiterOrLastRelease()
    {
        // 等待方在超时后才释放租约到达：TryDispose 与 ReleaseLease 兜底不能双重释放。
        var disposable = new FakeDisposable();
        var resource = new RefCountedResource<FakeDisposable>(disposable);
        resource.AcquireLease();

        Assert.False(await resource.RetireAndWaitForDrainAsync(TimeSpan.FromMilliseconds(20)));
        resource.ReleaseLease();
        resource.TryDispose();

        Assert.Equal(1, disposable.DisposeCalls);
    }

    private sealed class FakeDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }
}
