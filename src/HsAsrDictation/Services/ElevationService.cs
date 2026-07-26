using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace HsAsrDictation.Services;

public sealed class ElevationService
{
    private const int UserCanceledNativeErrorCode = 1223;
    private const int LauncherTimeoutMilliseconds = 30_000;
    private const int ProcessTerminationTimeoutMilliseconds = 5_000;

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

    public ElevationRestartResult RestartAsAdministrator()
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
            var commandInterpreterPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var launcherPath = Path.Combine(
                Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory,
                StartupRegistrationCommandBuilder.AdministratorLauncherFileName);
            if (!File.Exists(commandInterpreterPath) || !File.Exists(launcherPath))
            {
                return ElevationRestartResult.CreateFailed(
                    "管理员启动所需的命令解释器或安全启动包装文件不存在。");
            }

            using var identity = WindowsIdentity.GetCurrent();
            var currentUserSid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(currentUserSid))
            {
                return ElevationRestartResult.CreateFailed("无法解析当前 Windows 用户 SID。");
            }

            var validationResult = WindowsStartupRegistrationSecurityValidator.Validate(
                processPath,
                currentUserSid);
            if (!validationResult.WasSuccessful)
            {
                return ElevationRestartResult.CreateFailed(
                    validationResult.Message ?? "管理员启动安全校验失败。");
            }

            var startInfo = new ProcessStartInfo(commandInterpreterPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            foreach (var arg in StartupRegistrationCommandBuilder.BuildElevatedLauncherArgumentList(
                         processPath,
                         commandInterpreterPath,
                         ElevatedLaunchMode.Administrator,
                         currentUserSid))
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ElevationRestartResult.CreateFailed("系统未返回已启动的管理员进程。");
            }

            if (!process.WaitForExit(LauncherTimeoutMilliseconds))
            {
                var wasTerminated = TryTerminateProcess(process);
                return ElevationRestartResult.CreateFailed(
                    wasTerminated
                        ? "管理员启动包装进程等待超时，已终止；当前实例将继续运行。"
                        : "管理员启动包装进程等待超时且无法终止，状态未知；当前实例将继续运行。");
            }

            if (process.ExitCode != 0)
            {
                if (process.ExitCode == StartupOptions.ElevationOriginMismatchExitCode)
                {
                    return ElevationRestartResult.CreateFailed(
                        "UAC 凭据账号与当前 Windows 账号不一致，已拒绝管理员模式启动。");
                }

                return ElevationRestartResult.CreateFailed(
                    $"管理员启动包装进程失败，退出代码：{process.ExitCode}。");
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

    private static bool TryTerminateProcess(Process process)
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
