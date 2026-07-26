namespace HsAsrDictation.Services;

public sealed class StartupOptions
{
    public const string AdminFlag = "--admin";
    public const string AutoStartFlag = "--autostart";
    public const string ElevationAppliedFlag = "--elevation-applied";
    public const string ElevationOriginCheckFlag = "--elevation-origin-check";
    public const string ElevationOriginUserSidPrefix = "--elevation-origin-user-sid=";
    public const int ElevationOriginMismatchExitCode = 5;
    public const string StartupTaskMaintenancePrefix = "--startup-task-maintenance=";
    public const string StartupTaskUserSidPrefix = "--startup-task-user-sid=";

    public bool RequestAdministrator { get; init; }

    public bool IsAutoStart { get; init; }

    public bool ElevationApplied { get; init; }

    public bool IsElevationOriginCheck { get; init; }

    public string? ElevationOriginUserSid { get; init; }

    public StartupTaskMaintenanceAction StartupTaskMaintenance { get; init; }

    public string? StartupTaskUserSid { get; init; }

    public IReadOnlyList<string> ForwardedArgs { get; init; } = [];

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var forwardedArgs = new List<string>();
        var requestAdministrator = false;
        var isAutoStart = false;
        var elevationApplied = false;
        var isElevationOriginCheck = false;
        string? elevationOriginUserSid = null;
        var startupTaskMaintenance = StartupTaskMaintenanceAction.None;
        string? startupTaskUserSid = null;

        foreach (var arg in args)
        {
            if (string.Equals(arg, AdminFlag, StringComparison.OrdinalIgnoreCase))
            {
                requestAdministrator = true;
                continue;
            }

            if (string.Equals(arg, AutoStartFlag, StringComparison.OrdinalIgnoreCase))
            {
                isAutoStart = true;
                continue;
            }

            if (string.Equals(arg, ElevationAppliedFlag, StringComparison.OrdinalIgnoreCase))
            {
                elevationApplied = true;
                continue;
            }

            if (string.Equals(arg, ElevationOriginCheckFlag, StringComparison.OrdinalIgnoreCase))
            {
                isElevationOriginCheck = true;
                continue;
            }

            if (TryParseValue(arg, ElevationOriginUserSidPrefix, out var originUserSid))
            {
                elevationOriginUserSid = originUserSid;
                continue;
            }

            if (TryParseStartupTaskMaintenance(arg, out var maintenanceAction))
            {
                startupTaskMaintenance = maintenanceAction;
                continue;
            }

            if (TryParseStartupTaskUserSid(arg, out var userSid))
            {
                startupTaskUserSid = userSid;
                continue;
            }

            forwardedArgs.Add(arg);
        }

        return new StartupOptions
        {
            RequestAdministrator = requestAdministrator,
            IsAutoStart = isAutoStart,
            ElevationApplied = elevationApplied,
            IsElevationOriginCheck = isElevationOriginCheck,
            ElevationOriginUserSid = elevationOriginUserSid,
            StartupTaskMaintenance = startupTaskMaintenance,
            StartupTaskUserSid = startupTaskUserSid,
            ForwardedArgs = forwardedArgs
        };
    }

    public bool ShouldRestartAsAdministrator(bool isRunningAsAdministrator) =>
        RequestAdministrator && !ElevationApplied && !isRunningAsAdministrator;

    public bool IsElevationOriginCurrent(string? currentUserSid) =>
        (!ElevationApplied && !IsElevationOriginCheck) ||
        (!string.IsNullOrWhiteSpace(ElevationOriginUserSid) &&
         !string.IsNullOrWhiteSpace(currentUserSid) &&
         string.Equals(ElevationOriginUserSid, currentUserSid, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> BuildElevatedRestartArgs()
    {
        var args = new List<string>(ForwardedArgs.Count + 3);
        args.AddRange(ForwardedArgs);

        if (StartupTaskMaintenance != StartupTaskMaintenanceAction.None)
        {
            args.Add(BuildStartupTaskMaintenanceArg(StartupTaskMaintenance));

            if (!string.IsNullOrWhiteSpace(StartupTaskUserSid))
            {
                args.Add($"{StartupTaskUserSidPrefix}{StartupTaskUserSid}");
            }
        }

        args.Add(AdminFlag);
        args.Add(ElevationAppliedFlag);
        return args;
    }

    private static bool TryParseStartupTaskMaintenance(
        string arg,
        out StartupTaskMaintenanceAction maintenanceAction)
    {
        maintenanceAction = StartupTaskMaintenanceAction.None;
        if (!arg.StartsWith(StartupTaskMaintenancePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = arg[StartupTaskMaintenancePrefix.Length..];
        if (string.Equals(value, "create", StringComparison.OrdinalIgnoreCase))
        {
            maintenanceAction = StartupTaskMaintenanceAction.Create;
            return true;
        }

        if (string.Equals(value, "delete", StringComparison.OrdinalIgnoreCase))
        {
            maintenanceAction = StartupTaskMaintenanceAction.Delete;
            return true;
        }

        return false;
    }

    private static string BuildStartupTaskMaintenanceArg(StartupTaskMaintenanceAction maintenanceAction) =>
        $"{StartupTaskMaintenancePrefix}{maintenanceAction.ToString().ToLowerInvariant()}";

    private static bool TryParseStartupTaskUserSid(string arg, out string? userSid)
        => TryParseValue(arg, StartupTaskUserSidPrefix, out userSid);

    private static bool TryParseValue(string arg, string prefix, out string? value)
    {
        value = null;
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parsedValue = arg[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(parsedValue))
        {
            return false;
        }

        value = parsedValue;
        return true;
    }
}

public enum StartupTaskMaintenanceAction
{
    None,
    Create,
    Delete
}
