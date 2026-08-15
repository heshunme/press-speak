using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace HsAsrDictation.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistrationPlatform : IStartupRegistrationPlatform
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "Press Speak";
    public const int MaintenanceSucceededExitCode = 0;
    public const int MaintenanceFailedExitCode = 1;

    private const int UserCanceledNativeErrorCode = 1223;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private const int PathNotFoundHResult = unchecked((int)0x80070003);
    private const int TaskNotFoundHResult = unchecked((int)0x8004130F);
    private const int TaskCreateOrUpdate = 6;
    private const int TaskDontAddPrincipalAce = 0x10;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskSecurityInformation = 0x00000007;
    private const int SchtasksTimeoutMilliseconds = 30_000;
    private const int MaintenanceTimeoutMilliseconds = 120_000;
    private const int RedirectedStreamTimeoutMilliseconds = 5_000;

    private readonly string _taskName;

    public WindowsStartupRegistrationPlatform()
        : this(ResolveExecutablePath(), ResolveCurrentUserSid())
    {
    }

    public WindowsStartupRegistrationPlatform(string executablePath, string userSid)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("当前可执行文件路径不能为空。", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("当前用户 SID 不能为空。", nameof(userSid));
        }

        ExecutablePath = executablePath.Trim();
        UserSid = new SecurityIdentifier(userSid.Trim()).Value;
        CommandInterpreterPath = WindowsProcessHelper.ResolveSystemBinary("cmd.exe");
        _taskName = StartupRegistrationCommandBuilder.BuildTaskName(UserSid);
    }

    public string ExecutablePath { get; }

    public string CommandInterpreterPath { get; }

    public string UserSid { get; }

    public string? GetRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(
            RunValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetRunCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 登录启动注册表项。");
        key.SetValue(RunValueName, command, RegistryValueKind.String);
    }

    public void DeleteRunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    public StartupTaskQueryResult QueryAdministratorTask()
    {
        try
        {
            var commandResult = RunSchtasks(["/Query", "/TN", _taskName, "/XML", "/HRESULT"]);
            if (commandResult.ExitCode == 0)
            {
                bool? securityDescriptorIsProtected = null;
                string? securityDescriptorMessage = null;
                try
                {
                    var securityDescriptor = QueryTaskSecurityDescriptorWithTimeout();
                    securityDescriptorIsProtected = StartupTaskSecurityDescriptor.IsProtectedForUser(
                        securityDescriptor,
                        UserSid);
                    if (!securityDescriptorIsProtected.Value)
                    {
                        securityDescriptorMessage = "计划任务安全描述符不符合保护策略。";
                    }
                }
                catch (Exception ex)
                {
                    securityDescriptorMessage = $"读取计划任务安全描述符失败：{ex.Message}";
                }

                return StartupRegistrationCommandBuilder.ParseScheduledTaskXml(
                    commandResult.StandardOutput,
                    securityDescriptorIsProtected,
                    securityDescriptorMessage);
            }

            if (IsTaskNotFoundExitCode(commandResult.ExitCode))
            {
                return StartupTaskQueryResult.NotFound();
            }

            return StartupTaskQueryResult.Failed(
                BuildSchtasksFailureMessage("查询", commandResult));
        }
        catch (Exception ex)
        {
            return StartupTaskQueryResult.Failed($"查询开机自启计划任务失败：{ex.Message}");
        }
    }

    public StartupRegistrationChangeResult ValidateAdministratorRegistration()
    {
        try
        {
            _ = StartupRegistrationCommandBuilder.BuildAdministratorTaskArguments(
                ExecutablePath,
                CommandInterpreterPath);
            var launcherPath = Path.Combine(
                GetWorkingDirectory(ExecutablePath),
                StartupRegistrationCommandBuilder.AdministratorLauncherFileName);
            if (!File.Exists(launcherPath))
            {
                return StartupRegistrationChangeResult.Failed(
                    $"管理员自动启动包装文件不存在：{launcherPath}");
            }
        }
        catch (Exception ex)
        {
            return StartupRegistrationChangeResult.Failed(ex.Message);
        }

        return WindowsStartupRegistrationSecurityValidator.Validate(ExecutablePath, UserSid);
    }

    public StartupRegistrationMaintenanceResult RequestMaintenance(StartupTaskMaintenanceAction action)
    {
        if (action == StartupTaskMaintenanceAction.None)
        {
            return StartupRegistrationMaintenanceResult.Failed("未指定计划任务维护动作。");
        }

        if (IsRunningAsAdministrator())
        {
            return ExecuteMaintenance(action);
        }

        if (action == StartupTaskMaintenanceAction.Delete)
        {
            return RequestElevatedTaskDeletion();
        }

        var validationResult = ValidateAdministratorRegistration();
        if (!validationResult.WasSuccessful)
        {
            return StartupRegistrationMaintenanceResult.Failed(
                validationResult.Message ?? "管理员登录自启动安全校验失败。");
        }

        try
        {
            var startInfo = new ProcessStartInfo(CommandInterpreterPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = GetWorkingDirectory(ExecutablePath),
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var launchMode = action == StartupTaskMaintenanceAction.Create
                ? ElevatedLaunchMode.MaintenanceCreate
                : ElevatedLaunchMode.MaintenanceDelete;
            foreach (var argument in StartupRegistrationCommandBuilder.BuildElevatedLauncherArgumentList(
                         ExecutablePath,
                         CommandInterpreterPath,
                         launchMode,
                         UserSid))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return StartupRegistrationMaintenanceResult.Failed("系统未返回已启动的计划任务维护进程。");
            }

            if (!process.WaitForExit(MaintenanceTimeoutMilliseconds))
            {
                var wasTerminated = WindowsProcessHelper.TryTerminateProcess(process);
                return StartupRegistrationMaintenanceResult.Failed(
                    WindowsProcessHelper.BuildWaitTimeoutMessage("计划任务维护进程", wasTerminated));
            }

            return process.ExitCode == MaintenanceSucceededExitCode
                ? StartupRegistrationMaintenanceResult.Succeeded()
                : StartupRegistrationMaintenanceResult.Failed(
                    $"计划任务维护进程失败，退出代码：{process.ExitCode}。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledNativeErrorCode)
        {
            return StartupRegistrationMaintenanceResult.Canceled("用户取消了管理员权限请求。");
        }
        catch (Exception ex)
        {
            return StartupRegistrationMaintenanceResult.Failed($"请求维护开机自启计划任务失败：{ex.Message}");
        }
    }

    public StartupRegistrationMaintenanceResult ExecuteMaintenance(StartupTaskMaintenanceAction action)
    {
        if (action == StartupTaskMaintenanceAction.None)
        {
            return StartupRegistrationMaintenanceResult.Failed("未指定计划任务维护动作。");
        }

        if (!IsRunningAsAdministrator())
        {
            return StartupRegistrationMaintenanceResult.Failed("维护管理员开机自启计划任务需要管理员权限。");
        }

        try
        {
            return action switch
            {
                StartupTaskMaintenanceAction.Create => CreateAdministratorTask(),
                StartupTaskMaintenanceAction.Delete => DeleteAdministratorTask(),
                _ => StartupRegistrationMaintenanceResult.Failed("不支持的计划任务维护动作。")
            };
        }
        catch (Exception ex)
        {
            return StartupRegistrationMaintenanceResult.Failed($"维护开机自启计划任务失败：{ex.Message}");
        }
    }

    public static int GetMaintenanceExitCode(StartupRegistrationMaintenanceResult result) =>
        result.WasSuccessful ? MaintenanceSucceededExitCode : MaintenanceFailedExitCode;

    private StartupRegistrationMaintenanceResult CreateAdministratorTask()
    {
        var validationResult = ValidateAdministratorRegistration();
        if (!validationResult.WasSuccessful)
        {
            return StartupRegistrationMaintenanceResult.Failed(
                validationResult.Message ?? "管理员登录自启动安全校验失败。");
        }

        var taskXml = StartupRegistrationCommandBuilder.BuildScheduledTaskXml(
            ExecutablePath,
            UserSid,
            _taskName,
            CommandInterpreterPath);
        object? schedulerService = null;
        object? rootFolder = null;
        object? registeredTask = null;

        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("无法加载 Windows 任务计划程序服务。");
            schedulerService = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("无法创建 Windows 任务计划程序服务。");

            dynamic scheduler = schedulerService;
            scheduler.Connect();
            rootFolder = scheduler.GetFolder("\\");

            dynamic folder = rootFolder;
            registeredTask = folder.RegisterTask(
                _taskName,
                taskXml,
                TaskCreateOrUpdate | TaskDontAddPrincipalAce,
                UserSid,
                null,
                TaskLogonInteractiveToken,
                StartupTaskSecurityDescriptor.Build(UserSid));

            var verificationResult = QueryAdministratorTask();
            if (StartupRegistrationCommandBuilder.MatchesAdministratorTask(
                    verificationResult,
                    ExecutablePath,
                    UserSid,
                    CommandInterpreterPath))
            {
                return StartupRegistrationMaintenanceResult.Succeeded();
            }

            var verificationMessage = verificationResult.Message ??
                                      "管理员登录自启动计划任务创建后校验失败。";
            try
            {
                folder.DeleteTask(_taskName, 0);
                return StartupRegistrationMaintenanceResult.Failed(
                    $"{verificationMessage} 已删除未确认的计划任务。");
            }
            catch (Exception cleanupException)
            {
                return StartupRegistrationMaintenanceResult.Failed(
                    $"{verificationMessage} 删除未确认的计划任务也失败：{cleanupException.Message}");
            }
        }
        finally
        {
            ReleaseComObject(registeredTask);
            ReleaseComObject(rootFolder);
            ReleaseComObject(schedulerService);
        }
    }

    private StartupRegistrationMaintenanceResult DeleteAdministratorTask()
    {
        var queryResult = QueryAdministratorTask();
        if (queryResult.WasNotFound)
        {
            return StartupRegistrationMaintenanceResult.Succeeded();
        }

        if (queryResult.WasFailed)
        {
            return StartupRegistrationMaintenanceResult.Failed(
                queryResult.Message ?? "无法查询待删除的开机自启计划任务。");
        }

        var commandResult = RunSchtasks(["/Delete", "/TN", _taskName, "/F"]);
        return commandResult.ExitCode == 0
            ? StartupRegistrationMaintenanceResult.Succeeded()
            : StartupRegistrationMaintenanceResult.Failed(
                BuildSchtasksFailureMessage("删除", commandResult));
    }

    private StartupRegistrationMaintenanceResult RequestElevatedTaskDeletion()
    {
        try
        {
            var schtasksPath = WindowsProcessHelper.ResolveSystemBinary("schtasks.exe");
            var startInfo = new ProcessStartInfo(schtasksPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.SystemDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/Delete");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(_taskName);
            startInfo.ArgumentList.Add("/F");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return StartupRegistrationMaintenanceResult.Failed(
                    "系统未返回已启动的计划任务删除进程。");
            }

            if (!process.WaitForExit(MaintenanceTimeoutMilliseconds))
            {
                var wasTerminated = WindowsProcessHelper.TryTerminateProcess(process);
                return StartupRegistrationMaintenanceResult.Failed(
                    WindowsProcessHelper.BuildWaitTimeoutMessage("计划任务删除进程", wasTerminated));
            }

            if (process.ExitCode == 0)
            {
                return StartupRegistrationMaintenanceResult.Succeeded();
            }

            var queryResult = QueryAdministratorTask();
            return queryResult.WasNotFound
                ? StartupRegistrationMaintenanceResult.Succeeded()
                : StartupRegistrationMaintenanceResult.Failed(
                    $"删除管理员开机自启计划任务失败，退出代码：{process.ExitCode}。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledNativeErrorCode)
        {
            return StartupRegistrationMaintenanceResult.Canceled("用户取消了管理员权限请求。");
        }
        catch (Exception ex)
        {
            return StartupRegistrationMaintenanceResult.Failed(
                $"请求删除管理员开机自启计划任务失败：{ex.Message}");
        }
    }

    private static SchtasksCommandResult RunSchtasks(IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("计划任务维护仅支持 Windows。");
        }

        var schtasksPath = WindowsProcessHelper.ResolveSystemBinary("schtasks.exe");

        var startInfo = new ProcessStartInfo(schtasksPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(SchtasksTimeoutMilliseconds))
        {
            WindowsProcessHelper.TryTerminateProcess(process);
            throw new TimeoutException("等待 schtasks.exe 完成操作超时。");
        }

        if (!Task.WaitAll(
                [standardOutputTask, standardErrorTask],
                RedirectedStreamTimeoutMilliseconds))
        {
            throw new TimeoutException("读取 schtasks.exe 输出超时。");
        }

        return new SchtasksCommandResult(
            process.ExitCode,
            standardOutputTask.GetAwaiter().GetResult(),
            standardErrorTask.GetAwaiter().GetResult());
    }

    private static string BuildSchtasksFailureMessage(string operation, SchtasksCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? $"{operation}开机自启计划任务失败，退出代码：{result.ExitCode}。"
            : $"{operation}开机自启计划任务失败，退出代码：{result.ExitCode}。{detail}";
    }

    private static bool IsTaskNotFoundExitCode(int exitCode) =>
        exitCode is ErrorFileNotFound or ErrorPathNotFound or FileNotFoundHResult or PathNotFoundHResult or TaskNotFoundHResult;

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ResolveExecutablePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("无法解析当前可执行文件路径。");

    private static string ResolveCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new InvalidOperationException("无法解析当前用户 SID。");
    }

    private static string GetWorkingDirectory(string executablePath) =>
        Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;

    private string QueryTaskSecurityDescriptor()
    {
        object? schedulerService = null;
        object? rootFolder = null;
        object? registeredTask = null;

        try
        {
            var schedulerType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("无法加载 Windows 任务计划程序服务。");
            schedulerService = Activator.CreateInstance(schedulerType)
                ?? throw new InvalidOperationException("无法创建 Windows 任务计划程序服务。");

            dynamic scheduler = schedulerService;
            scheduler.Connect();
            rootFolder = scheduler.GetFolder("\\");

            dynamic folder = rootFolder;
            registeredTask = folder.GetTask(_taskName);
            dynamic task = registeredTask;
            return task.GetSecurityDescriptor(TaskSecurityInformation);
        }
        finally
        {
            ReleaseComObject(registeredTask);
            ReleaseComObject(rootFolder);
            ReleaseComObject(schedulerService);
        }
    }

    private string QueryTaskSecurityDescriptorWithTimeout()
    {
        var queryTask = Task.Run(QueryTaskSecurityDescriptor);
        var completedTask = Task.WhenAny(
                queryTask,
                Task.Delay(SchtasksTimeoutMilliseconds))
            .GetAwaiter()
            .GetResult();
        if (completedTask != queryTask)
        {
            throw new TimeoutException("读取计划任务安全描述符超时。");
        }

        return queryTask.GetAwaiter().GetResult();
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private sealed record SchtasksCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
