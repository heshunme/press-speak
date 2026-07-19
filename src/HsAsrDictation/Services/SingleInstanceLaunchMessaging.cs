namespace HsAsrDictation.Services;

public static class SingleInstanceLaunchMessaging
{
    public static string BuildAlreadyRunningMessage(
        PrivilegeMode? existingMode,
        PrivilegeMode currentMode,
        StartupOptions options)
    {
        if (options.ElevationApplied && currentMode == PrivilegeMode.Administrator)
        {
            return "管理员模式切换尚未完成，请稍后重试。";
        }

        if (existingMode == PrivilegeMode.Standard &&
            (options.RequestAdministrator || currentMode == PrivilegeMode.Administrator))
        {
            return "普通模式实例已在运行。请使用托盘菜单中的“以管理员模式重启”切换到管理员模式。";
        }

        return existingMode switch
        {
            PrivilegeMode.Administrator => "管理员模式实例已在运行，请勿重复启动。",
            PrivilegeMode.Standard => "普通模式实例已在运行，请勿重复启动。",
            _ => "程序已在运行，请勿重复启动。"
        };
    }
}
