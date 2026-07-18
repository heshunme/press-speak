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
            StartupOptions.ElevationAppliedFlag,
            "--sample"
        ]);

        Assert.True(options.RequestAdministrator);
        Assert.True(options.ElevationApplied);
        Assert.Equal(new[] { "--sample" }, options.ForwardedArgs);
    }

    [Fact]
    public void Parse_TreatsFlagsCaseInsensitively()
    {
        var options = StartupOptions.Parse([
            "--ADMIN",
            "--ELEVATION-APPLIED"
        ]);

        Assert.True(options.RequestAdministrator);
        Assert.True(options.ElevationApplied);
        Assert.Empty(options.ForwardedArgs);
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
    public void BuildElevatedRestartArgs_StripsDuplicateControlFlagsAndAppendsCanonicalFlags()
    {
        var options = StartupOptions.Parse([
            StartupOptions.AdminFlag,
            "value",
            StartupOptions.ElevationAppliedFlag,
            "--mode=test"
        ]);

        var args = options.BuildElevatedRestartArgs();

        Assert.Equal(
            new[]
            {
                "value",
                "--mode=test",
                StartupOptions.AdminFlag,
                StartupOptions.ElevationAppliedFlag
            },
            args);
    }
}
