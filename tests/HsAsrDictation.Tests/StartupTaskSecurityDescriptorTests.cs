using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class StartupTaskSecurityDescriptorTests
{
    private const string UserSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void IsProtectedForUser_AcceptsGeneratedDescriptor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var descriptor = StartupTaskSecurityDescriptor.Build(UserSid);

        Assert.True(StartupTaskSecurityDescriptor.IsProtectedForUser(descriptor, UserSid));
    }

    [Theory]
    [InlineData("O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;S-1-5-21-100-200-300-1001)")]
    [InlineData("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;S-1-5-21-100-200-300-1001)")]
    [InlineData("O:S-1-5-21-100-200-300-1001G:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;S-1-5-21-100-200-300-1001)")]
    [InlineData("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;S-1-5-21-100-200-300-1001)(A;;FW;;;BU)")]
    public void IsProtectedForUser_RejectsWeakDescriptor(string descriptor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(StartupTaskSecurityDescriptor.IsProtectedForUser(descriptor, UserSid));
    }
}
