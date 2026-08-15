using System.Diagnostics;
using System.IO;

namespace HsAsrDictation.Services;

/// <summary>
/// <see cref="WindowsStartupRegistrationPlatform"/>、<see cref="WindowsStartupAclRepairService"/>
/// 和 <see cref="ElevationService"/> 共用的进程辅助：定位系统目录下受信任的 Windows 自带
/// 可执行文件、等待超时后终止进程、拼接"等待超时"两条措辞消息。
/// </summary>
internal static class WindowsProcessHelper
{
    private const int ProcessTerminationTimeoutMilliseconds = 5_000;

    internal static string ResolveSystemBinary(string fileName)
    {
        var path = Path.Combine(Environment.SystemDirectory, fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"无法定位 Windows {fileName}。", path);
    }

    internal static bool TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return process.WaitForExit(ProcessTerminationTimeoutMilliseconds);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 拼接"提权进程等待超时"消息：进程名各异，收尾措辞默认是"请重新打开设置确认"那套
    /// （计划任务维护/删除、权限修复三处一致），ElevationService 的管理员启动包装进程
    /// 会传入自己的"当前实例将继续运行"收尾。
    /// </summary>
    internal static string BuildWaitTimeoutMessage(
        string processDisplayName,
        bool wasTerminated,
        string terminatedSuffix = "。请重新打开设置确认当前状态。",
        string unterminatedSuffix = "当前状态未知。请稍后重新打开设置确认。") =>
        wasTerminated
            ? $"{processDisplayName}等待超时，已终止{terminatedSuffix}"
            : $"{processDisplayName}等待超时且无法终止，{unterminatedSuffix}";
}
