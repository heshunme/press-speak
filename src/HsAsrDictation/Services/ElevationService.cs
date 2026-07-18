using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace HsAsrDictation.Services;

public sealed class ElevationService
{
    private const int UserCanceledNativeErrorCode = 1223;

    public bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public ElevationRestartResult RestartAsAdministrator(StartupOptions options)
    {
        if (IsRunningAsAdministrator())
        {
            return ElevationRestartResult.CreateNotRequested();
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return ElevationRestartResult.CreateFailed("无法解析当前可执行文件路径。");
        }

        try
        {
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory
            };

            foreach (var arg in options.BuildElevatedRestartArgs())
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return ElevationRestartResult.CreateFailed("系统未返回已启动的管理员进程。");
            }

            return ElevationRestartResult.CreateStarted();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledNativeErrorCode)
        {
            return ElevationRestartResult.CreateCanceled("用户取消了管理员权限请求。");
        }
        catch (Exception ex)
        {
            return ElevationRestartResult.CreateFailed(ex.Message);
        }
    }
}

public sealed class ElevationRestartResult
{
    public ElevationRestartStatus Status { get; init; }

    public string? Message { get; init; }

    public bool WasStarted => Status == ElevationRestartStatus.Started;

    public bool WasCanceled => Status == ElevationRestartStatus.Canceled;

    public bool WasFailed => Status == ElevationRestartStatus.Failed;

    public static ElevationRestartResult CreateNotRequested() => new()
    {
        Status = ElevationRestartStatus.NotRequested
    };

    public static ElevationRestartResult CreateStarted() => new()
    {
        Status = ElevationRestartStatus.Started
    };

    public static ElevationRestartResult CreateCanceled(string message) => new()
    {
        Status = ElevationRestartStatus.Canceled,
        Message = message
    };

    public static ElevationRestartResult CreateFailed(string message) => new()
    {
        Status = ElevationRestartStatus.Failed,
        Message = message
    };
}

public enum ElevationRestartStatus
{
    NotRequested,
    Started,
    Canceled,
    Failed
}
