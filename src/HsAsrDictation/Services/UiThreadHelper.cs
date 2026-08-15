namespace HsAsrDictation.Services;

/// <summary>
/// 在 WPF UI 线程上同步执行操作的共享帮助方法：当前线程已是 UI 线程
/// （或 Dispatcher 不可用）时直接执行，否则通过 Dispatcher 同步派发。
/// </summary>
internal static class UiThreadHelper
{
    public static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
