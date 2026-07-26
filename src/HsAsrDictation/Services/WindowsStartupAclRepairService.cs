using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace HsAsrDictation.Services;

/// <summary>
/// 修复自定义安装目录（如非 C 盘）因属主/DACL 允许普通用户写入而导致管理员模式提权被拒绝的问题。
///
/// 安全前提（不可绕过）：提权对象永远只能是 Windows 自带、位置固定受信任的 icacls.exe/cmd.exe，
/// 绝不能是应用自己的 exe 或安装目录里的 .cmd 包装文件——否则等于在目录仍可被普通用户写入的
/// 情况下就提权执行了"可能已被同权限攻击者篡改的内容"，正是
/// <see cref="WindowsStartupRegistrationSecurityValidator"/> 原本要挡住的攻击。修复是否真正成功
/// 必须由调用方在修复后重新调用未改动的 <see cref="WindowsStartupRegistrationSecurityValidator.Validate"/>
/// 判定，本类的返回值不能作为"可以提权"的依据。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupAclRepairService
{
    private const int UserCanceledNativeErrorCode = 1223;
    private const int RepairTimeoutMilliseconds = 120_000;
    private const int ProcessTerminationTimeoutMilliseconds = 5_000;

    public AclRepairResult Repair(string applicationDirectory, string userSid)
    {
        string? diagnosticLogPath = null;
        try
        {
            var normalizedDirectory = Path.GetFullPath(applicationDirectory);
            if (!Directory.Exists(normalizedDirectory))
            {
                return AclRepairResult.Failed($"待修复目录不存在：{normalizedDirectory}");
            }

            if (WindowsStartupRegistrationSecurityValidator.HasHardLink(normalizedDirectory, out var preExistingHardLink))
            {
                return AclRepairResult.Failed(
                    $"目录内含硬链接，无法安全自动修复：{preExistingHardLink}。请手动处理或更换安装位置。");
            }

            var icaclsPath = ResolveIcaclsPath();
            var commandInterpreterPath = ResolveCommandInterpreterPath();
            var commands = StartupRegistrationCommandBuilder.BuildAclRepairCommands(
                icaclsPath,
                normalizedDirectory,
                ownerSid: WindowsStartupRegistrationSecurityValidator.AdministratorsSid,
                trustedFullControlSids:
                [
                    WindowsStartupRegistrationSecurityValidator.LocalSystemSid,
                    WindowsStartupRegistrationSecurityValidator.AdministratorsSid,
                    WindowsStartupRegistrationSecurityValidator.TrustedInstallerSid
                ],
                readExecuteSid: userSid);

            diagnosticLogPath = Path.Combine(Path.GetTempPath(), $"press-speak-acl-repair-{Guid.NewGuid():N}.log");

            // ProcessStartInfo.ArgumentList 会按 Win32 CommandLineToArgvW 规则对每个元素做
            // 反斜杠转义再整体加引号，和 cmd.exe 自己的引号剥离规则冲突，产生乱码命令行；
            // 因此改用 Arguments 原始字符串。具体的引号/重定向拼接规则（含 cmd.exe /C
            // "剥离首尾引号"这条极易踩坑的规则）见 BuildAclRepairElevatedArguments 的说明——
            // 那里已经踩过两次真机上"看起来像什么都没发生"的坑，不要在这里重新手工拼接。
            var startInfo = new ProcessStartInfo(commandInterpreterPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.SystemDirectory,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = StartupRegistrationCommandBuilder.BuildAclRepairElevatedArguments(
                    commands,
                    diagnosticLogPath)
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return AclRepairResult.Failed("系统未返回已启动的权限修复进程。");
            }

            if (!process.WaitForExit(RepairTimeoutMilliseconds))
            {
                var wasTerminated = TryTerminateProcess(process);
                return AclRepairResult.Failed(
                    wasTerminated
                        ? "权限修复进程等待超时，已终止。请重新打开设置确认当前状态。"
                        : "权限修复进程等待超时且无法终止，当前状态未知。请稍后重新打开设置确认。");
            }

            if (process.ExitCode != 0)
            {
                return AclRepairResult.Failed(
                    $"权限修复进程失败，退出代码：{process.ExitCode}。{ReadDiagnosticLog(diagnosticLogPath)}");
            }

            if (WindowsStartupRegistrationSecurityValidator.HasHardLink(normalizedDirectory, out var postRepairHardLink))
            {
                return AclRepairResult.Failed(
                    $"权限修复后复查发现硬链接，视为不安全，未继续：{postRepairHardLink}。");
            }

            return AclRepairResult.Succeeded();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledNativeErrorCode)
        {
            return AclRepairResult.Canceled("用户取消了权限修复的管理员权限请求。");
        }
        catch (Exception ex)
        {
            return AclRepairResult.Failed(ex.Message);
        }
        finally
        {
            TryDeleteDiagnosticLog(diagnosticLogPath);
        }
    }

    private static string ReadDiagnosticLog(string path)
    {
        try
        {
            var content = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
            return string.IsNullOrWhiteSpace(content) ? string.Empty : $"icacls 输出：{content}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryDeleteDiagnosticLog(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 忽略清理失败，不影响修复结果。
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

    private static string ResolveIcaclsPath()
    {
        var path = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("无法定位 Windows icacls.exe。", path);
    }

    private static string ResolveCommandInterpreterPath()
    {
        var path = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("无法定位 Windows cmd.exe。", path);
    }
}

public enum AclRepairStatus
{
    Succeeded,
    Canceled,
    Failed
}

public sealed class AclRepairResult
{
    public AclRepairStatus Status { get; init; }

    public string? Message { get; init; }

    public bool WasSuccessful => Status == AclRepairStatus.Succeeded;

    public bool WasCanceled => Status == AclRepairStatus.Canceled;

    public bool WasFailed => Status == AclRepairStatus.Failed;

    public static AclRepairResult Succeeded() => new() { Status = AclRepairStatus.Succeeded };

    public static AclRepairResult Canceled(string message) => new()
    {
        Status = AclRepairStatus.Canceled,
        Message = message
    };

    public static AclRepairResult Failed(string message) => new()
    {
        Status = AclRepairStatus.Failed,
        Message = message
    };
}
