using System.Xml.Linq;

namespace HsAsrDictation.Services;

public static class StartupRegistrationCommandBuilder
{
    public const string AutoStartArgument = StartupOptions.AutoStartFlag;
    public const int MaxRunCommandLength = 260;
    public const string TaskNamePrefix = "Press Speak Startup";
    public const string AdministratorLauncherFileName = "press-speak-admin-autostart.cmd";

    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string BuildRunCommand(string executablePath)
    {
        var validatedPath = ValidateExecutablePath(executablePath);
        var command = $"\"{validatedPath}\" {AutoStartArgument}";
        if (command.Length > MaxRunCommandLength)
        {
            throw new InvalidOperationException(
                $"Windows 登录启动命令不能超过 {MaxRunCommandLength} 个字符。当前长度：{command.Length}。");
        }

        return command;
    }

    public static string BuildTaskName(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("当前用户 SID 不能为空。", nameof(userSid));
        }

        var normalizedSid = string.Concat(userSid.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_'));
        return $"{TaskNamePrefix} {normalizedSid}";
    }

    public static string BuildScheduledTaskXml(
        string executablePath,
        string userSid,
        string taskName,
        string commandInterpreterPath)
    {
        var validatedPath = ValidateExecutablePath(executablePath);
        var validatedCommandInterpreterPath = ValidateExecutablePath(commandInterpreterPath);
        var administratorTaskArguments = BuildAdministratorTaskArguments(
            validatedPath,
            validatedCommandInterpreterPath);
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("当前用户 SID 不能为空。", nameof(userSid));
        }

        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("计划任务名称不能为空。", nameof(taskName));
        }

        var principalId = "CurrentUser";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(TaskNamespace + "Task",
                new XAttribute("version", "1.4"),
                new XElement(TaskNamespace + "RegistrationInfo",
                    new XElement(TaskNamespace + "Author", "Press Speak"),
                    new XElement(TaskNamespace + "URI", $"\\{taskName}")),
                new XElement(TaskNamespace + "Triggers",
                    new XElement(TaskNamespace + "LogonTrigger",
                        new XElement(TaskNamespace + "Enabled", true),
                        new XElement(TaskNamespace + "UserId", userSid.Trim()))),
                new XElement(TaskNamespace + "Principals",
                    new XElement(TaskNamespace + "Principal",
                        new XAttribute("id", principalId),
                        new XElement(TaskNamespace + "UserId", userSid.Trim()),
                        new XElement(TaskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(TaskNamespace + "RunLevel", "HighestAvailable"))),
                new XElement(TaskNamespace + "Settings",
                    new XElement(TaskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(TaskNamespace + "DisallowStartIfOnBatteries", false),
                    new XElement(TaskNamespace + "StopIfGoingOnBatteries", false),
                    new XElement(TaskNamespace + "AllowHardTerminate", true),
                    new XElement(TaskNamespace + "StartWhenAvailable", true),
                    new XElement(TaskNamespace + "RunOnlyIfNetworkAvailable", false),
                    new XElement(TaskNamespace + "IdleSettings",
                        new XElement(TaskNamespace + "StopOnIdleEnd", false),
                        new XElement(TaskNamespace + "RestartOnIdle", false)),
                    new XElement(TaskNamespace + "AllowStartOnDemand", true),
                    new XElement(TaskNamespace + "Enabled", true),
                    new XElement(TaskNamespace + "Hidden", false),
                    new XElement(TaskNamespace + "RunOnlyIfIdle", false),
                    new XElement(TaskNamespace + "WakeToRun", false),
                    new XElement(TaskNamespace + "ExecutionTimeLimit", "PT0S"),
                    new XElement(TaskNamespace + "Priority", 7)),
                new XElement(TaskNamespace + "Actions",
                    new XAttribute("Context", principalId),
                    new XElement(TaskNamespace + "Exec",
                        new XElement(TaskNamespace + "Command", validatedCommandInterpreterPath),
                        new XElement(TaskNamespace + "Arguments", administratorTaskArguments),
                        new XElement(TaskNamespace + "WorkingDirectory", GetWorkingDirectory(validatedPath))))));

        return $"{document.Declaration}{Environment.NewLine}{document.ToString(SaveOptions.DisableFormatting)}";
    }

    public static StartupTaskQueryResult ParseScheduledTaskXml(
        string xml,
        bool? securityDescriptorIsProtected = null,
        string? securityDescriptorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return StartupTaskQueryResult.Failed("计划任务查询未返回 XML。");
        }

        try
        {
            var document = XDocument.Parse(xml);
            var taskNamespace = document.Root?.Name.Namespace ?? TaskNamespace;
            var actionContainers = document.Root?
                .Elements(taskNamespace + "Actions")
                .ToList() ?? [];
            var actions = actionContainers.SelectMany(element => element.Elements()).ToList();
            var triggerContainers = document.Root?
                .Elements(taskNamespace + "Triggers")
                .ToList() ?? [];
            var triggers = triggerContainers.SelectMany(element => element.Elements()).ToList();
            var exec = actions.FirstOrDefault(element => element.Name == taskNamespace + "Exec");
            var principal = document.Descendants(taskNamespace + "Principal").FirstOrDefault();
            var logonTrigger = triggers.FirstOrDefault(
                element => element.Name == taskNamespace + "LogonTrigger");
            var settings = document.Root?.Element(taskNamespace + "Settings");

            return StartupTaskQueryResult.Found(
                exec?.Element(taskNamespace + "Command")?.Value,
                exec?.Element(taskNamespace + "Arguments")?.Value,
                exec?.Element(taskNamespace + "WorkingDirectory")?.Value,
                actionContainers.Count == 1 ? actions.Count : -1,
                triggerContainers.Count == 1 ? triggers.Count : -1,
                principal?.Element(taskNamespace + "UserId")?.Value,
                logonTrigger?.Element(taskNamespace + "UserId")?.Value,
                ParseBoolean(logonTrigger?.Element(taskNamespace + "Enabled")?.Value),
                principal?.Element(taskNamespace + "LogonType")?.Value,
                principal?.Element(taskNamespace + "RunLevel")?.Value,
                settings?.Element(taskNamespace + "MultipleInstancesPolicy")?.Value,
                settings?.Element(taskNamespace + "ExecutionTimeLimit")?.Value,
                ParseBoolean(settings?.Element(taskNamespace + "Enabled")?.Value),
                ParseBoolean(settings?.Element(taskNamespace + "DisallowStartIfOnBatteries")?.Value),
                ParseBoolean(settings?.Element(taskNamespace + "StopIfGoingOnBatteries")?.Value),
                securityDescriptorIsProtected,
                securityDescriptorMessage);
        }
        catch (Exception ex)
        {
            return StartupTaskQueryResult.Failed($"解析计划任务 XML 失败：{ex.Message}");
        }
    }

    public static bool MatchesAdministratorTask(
        StartupTaskQueryResult queryResult,
        string executablePath,
        string userSid,
        string commandInterpreterPath)
    {
        if (!queryResult.WasFound)
        {
            return false;
        }

        return string.Equals(
                   NormalizeWindowsPath(queryResult.ExecutablePath),
                   NormalizeWindowsPath(commandInterpreterPath),
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   queryResult.Arguments?.Trim(),
                   BuildAdministratorTaskArguments(executablePath, commandInterpreterPath),
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   NormalizeWindowsPath(queryResult.WorkingDirectory),
                   NormalizeWindowsPath(GetWorkingDirectory(executablePath)),
                   StringComparison.OrdinalIgnoreCase) &&
               queryResult.ActionCount == 1 &&
               queryResult.TriggerCount == 1 &&
               string.Equals(queryResult.PrincipalUserId, userSid, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(queryResult.LogonTriggerUserId, userSid, StringComparison.OrdinalIgnoreCase) &&
               queryResult.LogonTriggerEnabled != false &&
               string.Equals(queryResult.LogonType, "InteractiveToken", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(queryResult.RunLevel, "HighestAvailable", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(queryResult.MultipleInstancesPolicy, "IgnoreNew", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(queryResult.ExecutionTimeLimit, "PT0S", StringComparison.OrdinalIgnoreCase) &&
               queryResult.TaskEnabled == true &&
               queryResult.DisallowStartIfOnBatteries == false &&
               queryResult.StopIfGoingOnBatteries == false &&
               queryResult.SecurityDescriptorIsProtected == true;
    }

    public static bool MatchesRunCommand(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        return string.Equals(
            command.Trim(),
            BuildRunCommand(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 拼接一次性 ACL 修复所需的三条 icacls 命令（各自独立，不做 <c>&amp;&amp;</c> 串联或输出
    /// 重定向——那是调用方在实际拼接提权命令行时才决定的执行细节，见下方"为什么不在这里串联"）：
    /// 先取得属主，再清除继承 ACE，最后精确授予"受信任主体完全控制 + 指定账号只读执行"。
    /// SID 一律由调用方传入（通常取自 <c>WindowsStartupRegistrationSecurityValidator</c> 的
    /// 信任主体常量），本方法不内置任何 SID，避免和校验器的信任判定产生第二份可能漂移的拷贝。
    /// 刻意不用 <c>/reset</c>——它会重新继承父目录 ACL，可能把刚清除的宽松授权重新引入。
    ///
    /// 为什么不在这里串联：每条命令的 <c>/grant:r</c> 权限规格里含有字面的 <c>(OI)(CI)</c> 括号；
    /// 如果调用方为了让重定向同时捕获全部输出而用外层 <c>( cmd1 &amp;&amp; cmd2 &amp;&amp; cmd3 ) &gt; log</c>
    /// 这种括号分组包裹整条链，cmd.exe 的分组括号匹配会和 icacls 参数里的字面括号冲突，
    /// 导致语法错误（曾经在真机上复现，退出代码 1、且重定向从未生效）。正确做法是每条命令各自
    /// 独立重定向（第一条 <c>&gt;</c>，其余 <c>&gt;&gt;</c> 追加）后再用 <c>&amp;&amp;</c> 连接，
    /// 不做任何括号分组——因此这里返回未拼接的命令列表，交给调用方逐条加重定向。
    /// </summary>
    public static IReadOnlyList<string> BuildAclRepairCommands(
        string icaclsPath,
        string directory,
        string ownerSid,
        IReadOnlyList<string> trustedFullControlSids,
        string readExecuteSid)
    {
        var validatedIcaclsPath = ValidateExecutablePath(icaclsPath);
        ValidateCmdSafePath(validatedIcaclsPath);
        var validatedDirectory = ValidateExecutablePath(directory);
        ValidateCmdSafePath(validatedDirectory);

        if (trustedFullControlSids.Count == 0)
        {
            throw new ArgumentException("必须至少指定一个受信任的完全控制主体。", nameof(trustedFullControlSids));
        }

        var grantEntries = string.Join(
            ' ',
            trustedFullControlSids
                .Select(sid => $"*{sid}:(OI)(CI)F")
                .Append($"*{readExecuteSid}:(OI)(CI)(RX)"));

        return
        [
            $"\"{validatedIcaclsPath}\" \"{validatedDirectory}\" /setowner *{ownerSid} /T /C /Q",
            $"\"{validatedIcaclsPath}\" \"{validatedDirectory}\" /inheritance:r /T /C /Q",
            $"\"{validatedIcaclsPath}\" \"{validatedDirectory}\" /grant:r {grantEntries} /T /C /Q"
        ];
    }

    /// <summary>
    /// 把 <see cref="BuildAclRepairCommands"/> 产出的若干条命令串成一次提权 cmd.exe 会话的完整
    /// <c>Arguments</c>：每条命令各自重定向到同一个诊断日志（第一条新建、其余追加）后用
    /// <c>&amp;&amp;</c> 连接，再整体套一层外层引号。
    ///
    /// 外层引号是必需的，不是可有可无的美化：cmd.exe 的 <c>/C</c> 有一条文档记录的特殊规则——
    /// 当其后的命令行以引号开头、且不满足"整行只有两个引号且引号间是一个可执行文件名"这一
    /// 狭窄条件时，cmd 会转而剥离整条命令行里"第一个"和"最后一个"引号字符（而不是按配对剥离）。
    /// 我们的命令链本身以带引号的 icacls 路径开头、内部还有大量引号（含 <c>/grant:r</c> 权限
    /// 规格里的字面括号），如果不额外包一层引号，这条规则会从中间任意挖走两个引号，彻底打乱
    /// 配对，产生语法错误——真机复现过：退出代码 1、且重定向从未生效，看起来像"什么都没发生"。
    /// 套上这层刻意添加的外层引号后，cmd 的"剥离首尾引号"规则消耗的正是这一层，内部引号原样
    /// 保留、按预期解析。返回值必须整体赋给 <c>ProcessStartInfo.Arguments</c>（原始字符串），
    /// 不能再经过 <c>ArgumentList</c>（那会被 .NET 按 Win32 规则重新转义，产生同类问题）。
    /// </summary>
    public static string BuildAclRepairElevatedArguments(
        IReadOnlyList<string> repairCommands,
        string diagnosticLogPath)
    {
        if (repairCommands.Count == 0)
        {
            throw new ArgumentException("必须至少提供一条修复命令。", nameof(repairCommands));
        }

        var validatedLogPath = ValidateExecutablePath(diagnosticLogPath);
        ValidateCmdSafePath(validatedLogPath);

        var chainedCommand = string.Join(
            " && ",
            repairCommands.Select((command, index) => index == 0
                ? $"{command} > \"{validatedLogPath}\" 2>&1"
                : $"{command} >> \"{validatedLogPath}\" 2>&1"));

        return $"/D /Q /V:OFF /E:ON /C \"{chainedCommand}\"";
    }

    public static string BuildAdministratorTaskArguments(
        string executablePath,
        string commandInterpreterPath)
    {
        var paths = GetElevatedLauncherPaths(executablePath, commandInterpreterPath);
        return $"/D /Q /V:OFF /E:ON /C CALL \"{paths.LauncherPath}\" \"{paths.ExecutablePath}\" \"{paths.SystemDirectory}\" \"{paths.WindowsDirectory}\" autostart";
    }

    public static IReadOnlyList<string> BuildElevatedLauncherArgumentList(
        string executablePath,
        string commandInterpreterPath,
        ElevatedLaunchMode mode,
        string? userSid = null)
    {
        var requiresUserSid = mode is ElevatedLaunchMode.Administrator or
            ElevatedLaunchMode.MaintenanceCreate or
            ElevatedLaunchMode.MaintenanceDelete;
        if (requiresUserSid && string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("提升启动命令缺少发起用户 SID。", nameof(userSid));
        }

        var paths = GetElevatedLauncherPaths(executablePath, commandInterpreterPath);
        var arguments = new List<string>
        {
            "/D",
            "/Q",
            "/V:OFF",
            "/E:ON",
            "/C",
            "CALL",
            paths.LauncherPath,
            paths.ExecutablePath,
            paths.SystemDirectory,
            paths.WindowsDirectory,
            mode switch
            {
                ElevatedLaunchMode.AutoStart => "autostart",
                ElevatedLaunchMode.Administrator => "administrator",
                ElevatedLaunchMode.MaintenanceCreate => "maintenance-create",
                ElevatedLaunchMode.MaintenanceDelete => "maintenance-delete",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "不支持的提升启动模式。")
            }
        };
        if (!string.IsNullOrWhiteSpace(userSid))
        {
            arguments.Add(userSid.Trim());
        }

        return arguments;
    }

    private static ElevatedLauncherPaths GetElevatedLauncherPaths(
        string executablePath,
        string commandInterpreterPath)
    {
        var validatedPath = ValidateExecutablePath(executablePath);
        var validatedCommandInterpreterPath = ValidateExecutablePath(commandInterpreterPath);
        ValidateCmdSafePath(validatedPath);
        ValidateCmdSafePath(validatedCommandInterpreterPath);

        var launcherPath =
            $"{GetWorkingDirectory(validatedPath).TrimEnd('\\', '/')}\\{AdministratorLauncherFileName}";
        var systemDirectory = GetWorkingDirectory(validatedCommandInterpreterPath);
        var windowsDirectory = GetWorkingDirectory(systemDirectory);
        return new ElevatedLauncherPaths(
            validatedPath,
            launcherPath,
            systemDirectory,
            windowsDirectory);
    }

    internal static void ValidateCmdSafePath(string path)
    {
        if (path.IndexOfAny(['%', '!', '^', '&', '|', '<', '>', '(', ')', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException(
                "管理员启动路径不能包含 cmd.exe 命令元字符。请将应用安装到其他目录。");
        }
    }

    private static string ValidateExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("当前可执行文件路径不能为空。", nameof(executablePath));
        }

        var trimmedPath = executablePath.Trim();
        if (trimmedPath.Contains('"'))
        {
            throw new ArgumentException("当前可执行文件路径不能包含双引号。", nameof(executablePath));
        }

        return trimmedPath;
    }

    private static string NormalizeWindowsPath(string? path) =>
        path?.Trim().Trim('"').Replace('/', '\\') ?? string.Empty;

    private static bool? ParseBoolean(string? value) => bool.TryParse(value, out var parsed)
        ? parsed
        : null;

    private static string GetWorkingDirectory(string executablePath)
    {
        var lastSeparatorIndex = Math.Max(
            executablePath.LastIndexOf('\\'),
            executablePath.LastIndexOf('/'));
        return lastSeparatorIndex > 0
            ? executablePath[..lastSeparatorIndex]
            : string.Empty;
    }

    private sealed record ElevatedLauncherPaths(
        string ExecutablePath,
        string LauncherPath,
        string SystemDirectory,
        string WindowsDirectory);
}

public enum ElevatedLaunchMode
{
    AutoStart,
    Administrator,
    MaintenanceCreate,
    MaintenanceDelete
}
