using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SingleInstanceLaunchMessagingTests
{
    [Fact]
    public void BuildAlreadyRunningMessage_ReturnsStandardDuplicateMessage_ForStandardConflict()
    {
        var message = SingleInstanceLaunchMessaging.BuildAlreadyRunningMessage(
            PrivilegeMode.Standard,
            PrivilegeMode.Standard,
            StartupOptions.Parse([]));

        Assert.Equal("普通模式实例已在运行，请勿重复启动。", message);
    }

    [Fact]
    public void BuildAlreadyRunningMessage_ReturnsManualUpgradeMessage_ForAdminRequestAgainstStandardInstance()
    {
        var message = SingleInstanceLaunchMessaging.BuildAlreadyRunningMessage(
            PrivilegeMode.Standard,
            PrivilegeMode.Standard,
            StartupOptions.Parse([StartupOptions.AdminFlag]));

        Assert.Equal("普通模式实例已在运行。请使用托盘菜单中的“以管理员模式重启”切换到管理员模式。", message);
    }

    [Fact]
    public void BuildAlreadyRunningMessage_ReturnsAdministratorDuplicateMessage_ForAdministratorConflict()
    {
        var message = SingleInstanceLaunchMessaging.BuildAlreadyRunningMessage(
            PrivilegeMode.Administrator,
            PrivilegeMode.Standard,
            StartupOptions.Parse([]));

        Assert.Equal("管理员模式实例已在运行，请勿重复启动。", message);
    }

    [Fact]
    public void BuildAlreadyRunningMessage_ReturnsTransitionMessage_ForElevatedHandoffConflict()
    {
        var message = SingleInstanceLaunchMessaging.BuildAlreadyRunningMessage(
            PrivilegeMode.Standard,
            PrivilegeMode.Administrator,
            StartupOptions.Parse([StartupOptions.AdminFlag, StartupOptions.ElevationAppliedFlag]));

        Assert.Equal("管理员模式切换尚未完成，请稍后重试。", message);
    }
}
