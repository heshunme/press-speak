using System.Xml.Linq;

namespace HsAsrDictation.Services;

public static class StartupRegistrationCommandBuilder
{
    public const string AutoStartArgument = StartupOptions.AutoStartFlag;
    public const string MaintenanceArgumentPrefix = StartupOptions.StartupTaskMaintenancePrefix;
    public const string MaintenanceUserSidArgumentPrefix = StartupOptions.StartupTaskUserSidPrefix;
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

    public static string BuildMaintenanceArgument(StartupTaskMaintenanceAction action) => action switch
    {
        StartupTaskMaintenanceAction.Create => $"{MaintenanceArgumentPrefix}create",
        StartupTaskMaintenanceAction.Delete => $"{MaintenanceArgumentPrefix}delete",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "不支持的启动任务维护动作。")
    };

    public static string BuildMaintenanceUserSidArgument(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("当前用户 SID 不能为空。", nameof(userSid));
        }

        return $"{MaintenanceUserSidArgumentPrefix}{userSid.Trim()}";
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

    private static void ValidateCmdSafePath(string path)
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
