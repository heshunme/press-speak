using System.IO;
using System.Runtime.Versioning;
using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistrationSecurityValidatorTests
{
    private const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string StandardUserSid = "S-1-5-21-1-2-3-1002";

    [Theory]
    [InlineData("NTFS")]
    [InlineData("ReFS")]
    [InlineData("ntfs")]
    public void ValidateApplicationVolume_AcceptsFixedAclVolume(string driveFormat)
    {
        Assert.Null(WindowsStartupRegistrationSecurityValidator.ValidateApplicationVolume(
            DriveType.Fixed,
            driveFormat));
    }

    [Theory]
    [InlineData(DriveType.Network, "NTFS")]
    [InlineData(DriveType.Removable, "NTFS")]
    [InlineData(DriveType.CDRom, "NTFS")]
    [InlineData(DriveType.Ram, "NTFS")]
    [InlineData(DriveType.Fixed, "exFAT")]
    public void ValidateApplicationVolume_RejectsUnsupportedVolume(
        DriveType driveType,
        string driveFormat)
    {
        Assert.NotNull(WindowsStartupRegistrationSecurityValidator.ValidateApplicationVolume(
            driveType,
            driveFormat));
    }

    [Theory]
    [InlineData(@"C:\", @"\Device\HarddiskVolume4")]
    [InlineData(@"z:/", @"\Device\HarddiskVolume12")]
    public void ValidateDosDeviceTarget_AcceptsDirectLocalVolume(
        string rootPath,
        string deviceTarget)
    {
        Assert.Null(WindowsStartupRegistrationSecurityValidator.ValidateDosDeviceTarget(
            rootPath,
            deviceTarget));
    }

    [Theory]
    [InlineData(@"X:\", @"\??\C:\ProtectedApp")]
    [InlineData(@"X:\", @"\Device\Mup\server\share")]
    [InlineData(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\", @"\Device\HarddiskVolume4")]
    public void ValidateDosDeviceTarget_RejectsRedirectableOrUnsupportedRoot(
        string rootPath,
        string deviceTarget)
    {
        Assert.NotNull(WindowsStartupRegistrationSecurityValidator.ValidateDosDeviceTarget(
            rootPath,
            deviceTarget));
    }

    [Fact]
    public void ValidateFileSystemSecurityDescriptor_AllowsOnlyTrustedWriters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var securityDescriptor =
            $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{TrustedInstallerSid})(A;;FR;;;BU)";

        Assert.Null(WindowsStartupRegistrationSecurityValidator.ValidateFileSystemSecurityDescriptor(
            securityDescriptor,
            isDirectory: false));
    }

    [Fact]
    public void ValidateFileSystemSecurityDescriptor_AllowsInheritOnlyCreatorOwner()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string securityDescriptor =
            "O:BAG:BAD:P(A;OICIIO;FA;;;CO)(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)";

        Assert.Null(WindowsStartupRegistrationSecurityValidator.ValidateFileSystemSecurityDescriptor(
            securityDescriptor,
            isDirectory: true));
    }

    [Fact]
    public void ValidateFileSystemSecurityDescriptor_RejectsOtherWritableUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var securityDescriptor =
            $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FW;;;{StandardUserSid})";

        Assert.Equal(
            StandardUserSid,
            WindowsStartupRegistrationSecurityValidator.ValidateFileSystemSecurityDescriptor(
                securityDescriptor,
                isDirectory: false));
    }

    [Fact]
    public void ValidateFileSystemSecurityDescriptor_RejectsUntrustedOwner()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var securityDescriptor =
            $"O:{StandardUserSid}G:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)";

        Assert.Equal(
            StandardUserSid,
            WindowsStartupRegistrationSecurityValidator.ValidateFileSystemSecurityDescriptor(
                securityDescriptor,
                isDirectory: false));
    }
}
