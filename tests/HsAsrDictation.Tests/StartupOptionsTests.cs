using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class StartupOptionsTests
{
    [Fact]
    public void Parse_RecognizesAdminAndInternalFlags()
    {
        var options = StartupOptions.Parse([
            StartupOptions.AdminFlag,
            StartupOptions.AutoStartFlag,
            StartupOptions.ElevationAppliedFlag,
            StartupOptions.ElevationOriginCheckFlag,
            $"{StartupOptions.ElevationOriginUserSidPrefix}S-1-5-21-9-8-7-1001",
            $"{StartupOptions.StartupTaskMaintenancePrefix}create",
            $"{StartupOptions.StartupTaskUserSidPrefix}S-1-5-21-1-2-3-1001",
            "--sample"
        ]);

        Assert.True(options.RequestAdministrator);
        Assert.True(options.IsAutoStart);
        Assert.True(options.ElevationApplied);
        Assert.True(options.IsElevationOriginCheck);
        Assert.Equal("S-1-5-21-9-8-7-1001", options.ElevationOriginUserSid);
        Assert.Equal(StartupTaskMaintenanceAction.Create, options.StartupTaskMaintenance);
        Assert.Equal("S-1-5-21-1-2-3-1001", options.StartupTaskUserSid);
        Assert.Equal(new[] { "--sample" }, options.ForwardedArgs);
    }

    [Fact]
    public void Parse_TreatsFlagsCaseInsensitively()
    {
        var options = StartupOptions.Parse([
            "--ADMIN",
            "--AUTOSTART",
            "--ELEVATION-APPLIED",
            "--ELEVATION-ORIGIN-USER-SID=S-1-5-21-7-8-9-1002",
            "--STARTUP-TASK-MAINTENANCE=DELETE",
            "--STARTUP-TASK-USER-SID=S-1-5-21-4-5-6-1002"
        ]);

        Assert.True(options.RequestAdministrator);
        Assert.True(options.IsAutoStart);
        Assert.True(options.ElevationApplied);
        Assert.Equal("S-1-5-21-7-8-9-1002", options.ElevationOriginUserSid);
        Assert.Equal(StartupTaskMaintenanceAction.Delete, options.StartupTaskMaintenance);
        Assert.Equal("S-1-5-21-4-5-6-1002", options.StartupTaskUserSid);
        Assert.Empty(options.ForwardedArgs);
    }

    [Fact]
    public void Parse_UsesLastRecognizedMaintenanceAction_AndForwardsUnknownValues()
    {
        var unknownArg = $"{StartupOptions.StartupTaskMaintenancePrefix}repair";
        var options = StartupOptions.Parse([
            $"{StartupOptions.StartupTaskMaintenancePrefix}create",
            unknownArg,
            $"{StartupOptions.StartupTaskMaintenancePrefix}delete"
        ]);

        Assert.Equal(StartupTaskMaintenanceAction.Delete, options.StartupTaskMaintenance);
        Assert.Equal(new[] { unknownArg }, options.ForwardedArgs);
    }

    [Fact]
    public void ShouldRestartAsAdministrator_ReturnsTrue_OnlyForPendingNonAdminRequest()
    {
        var options = StartupOptions.Parse([StartupOptions.AdminFlag]);

        Assert.True(options.ShouldRestartAsAdministrator(isRunningAsAdministrator: false));
        Assert.False(options.ShouldRestartAsAdministrator(isRunningAsAdministrator: true));
        Assert.False(
            StartupOptions.Parse([StartupOptions.AdminFlag, StartupOptions.ElevationAppliedFlag])
                .ShouldRestartAsAdministrator(isRunningAsAdministrator: false));
        Assert.False(
            StartupOptions.Parse(["--sample"])
                .ShouldRestartAsAdministrator(isRunningAsAdministrator: false));
    }

    [Fact]
    public void IsElevationOriginCurrent_RejectsMissingOrDifferentCredentialAccount()
    {
        var options = StartupOptions.Parse([
            StartupOptions.AdminFlag,
            StartupOptions.ElevationAppliedFlag,
            $"{StartupOptions.ElevationOriginUserSidPrefix}S-1-5-21-1-2-3-1001"
        ]);

        Assert.True(options.IsElevationOriginCurrent("S-1-5-21-1-2-3-1001"));
        Assert.False(options.IsElevationOriginCurrent("S-1-5-21-1-2-3-1002"));
        Assert.False(options.IsElevationOriginCurrent(null));
        Assert.False(
            StartupOptions.Parse([StartupOptions.AdminFlag, StartupOptions.ElevationAppliedFlag])
                .IsElevationOriginCurrent("S-1-5-21-1-2-3-1001"));
        Assert.False(
            StartupOptions.Parse([StartupOptions.ElevationOriginCheckFlag])
                .IsElevationOriginCurrent("S-1-5-21-1-2-3-1001"));
        Assert.True(
            StartupOptions.Parse([
                    StartupOptions.ElevationOriginCheckFlag,
                    $"{StartupOptions.ElevationOriginUserSidPrefix}S-1-5-21-1-2-3-1001"
                ])
                .IsElevationOriginCurrent("S-1-5-21-1-2-3-1001"));
        Assert.True(
            StartupOptions.Parse([StartupOptions.AdminFlag])
                .IsElevationOriginCurrent("S-1-5-21-1-2-3-1002"));
    }

    [Fact]
    public void BuildElevatedRestartArgs_StripsDuplicateControlFlagsAndAppendsCanonicalFlags()
    {
        var options = StartupOptions.Parse([
            StartupOptions.AdminFlag,
            StartupOptions.AutoStartFlag,
            "value",
            StartupOptions.ElevationAppliedFlag,
            $"{StartupOptions.StartupTaskMaintenancePrefix}CREATE",
            $"{StartupOptions.StartupTaskUserSidPrefix}S-1-5-21-1-2-3-1001",
            "--mode=test"
        ]);

        var args = options.BuildElevatedRestartArgs();

        Assert.Equal(
            new[]
            {
                "value",
                "--mode=test",
                $"{StartupOptions.StartupTaskMaintenancePrefix}create",
                $"{StartupOptions.StartupTaskUserSidPrefix}S-1-5-21-1-2-3-1001",
                StartupOptions.AdminFlag,
                StartupOptions.ElevationAppliedFlag
            },
            args);

        Assert.DoesNotContain(StartupOptions.AutoStartFlag, args);
    }
}
