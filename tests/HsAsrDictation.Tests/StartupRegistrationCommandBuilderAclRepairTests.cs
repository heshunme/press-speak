using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class StartupRegistrationCommandBuilderAclRepairTests
{
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string UserSid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void BuildAclRepairCommands_ReturnsSetOwnerInheritanceAndGrantInOrder()
    {
        var commands = StartupRegistrationCommandBuilder.BuildAclRepairCommands(
            @"C:\Windows\System32\icacls.exe",
            @"D:\Program Files\press-speak",
            ownerSid: AdministratorsSid,
            trustedFullControlSids: [SystemSid, AdministratorsSid, TrustedInstallerSid],
            readExecuteSid: UserSid);

        Assert.Equal(3, commands.Count);

        Assert.Equal(
            $"\"C:\\Windows\\System32\\icacls.exe\" \"D:\\Program Files\\press-speak\" /setowner *{AdministratorsSid} /T /C /Q",
            commands[0]);
        Assert.Equal(
            "\"C:\\Windows\\System32\\icacls.exe\" \"D:\\Program Files\\press-speak\" /inheritance:r /T /C /Q",
            commands[1]);
        Assert.Equal(
            $"\"C:\\Windows\\System32\\icacls.exe\" \"D:\\Program Files\\press-speak\" /grant:r " +
            $"*{SystemSid}:(OI)(CI)F *{AdministratorsSid}:(OI)(CI)F *{TrustedInstallerSid}:(OI)(CI)F " +
            $"*{UserSid}:(OI)(CI)(RX) /T /C /Q",
            commands[2]);
    }

    [Fact]
    public void BuildAclRepairCommands_NeverUsesResetSwitch()
    {
        // /reset 会重新继承父目录 ACL，可能把刚清除的宽松授权重新引入——绝不能出现。
        var commands = StartupRegistrationCommandBuilder.BuildAclRepairCommands(
            @"C:\Windows\System32\icacls.exe",
            @"D:\press-speak",
            ownerSid: AdministratorsSid,
            trustedFullControlSids: [SystemSid],
            readExecuteSid: UserSid);

        Assert.All(commands, command =>
            Assert.DoesNotContain("/reset", command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildAclRepairCommands_IndividualCommandsContainNoOuterGroupingParens()
    {
        // 每条命令自身合法地含有字面括号 (OI)(CI)——但绝不能被再套一层外层分组括号，
        // 否则 cmd.exe 的分组匹配会和这些字面括号冲突（真机复现过的语法错误）。
        var commands = StartupRegistrationCommandBuilder.BuildAclRepairCommands(
            @"C:\Windows\System32\icacls.exe",
            @"D:\press-speak",
            ownerSid: AdministratorsSid,
            trustedFullControlSids: [SystemSid],
            readExecuteSid: UserSid);

        Assert.All(commands, command =>
        {
            Assert.False(command.StartsWith('('));
            Assert.DoesNotContain("&&", command, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(@"D:\press & speak")]
    [InlineData(@"D:\press%speak%")]
    [InlineData(@"D:\press(1)")]
    public void BuildAclRepairCommands_RejectsCmdUnsafeDirectoryPath(string unsafeDirectory)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupRegistrationCommandBuilder.BuildAclRepairCommands(
                @"C:\Windows\System32\icacls.exe",
                unsafeDirectory,
                ownerSid: AdministratorsSid,
                trustedFullControlSids: [SystemSid],
                readExecuteSid: UserSid));
    }

    [Fact]
    public void BuildAclRepairCommands_RejectsDirectoryContainingQuote()
    {
        Assert.Throws<ArgumentException>(() =>
            StartupRegistrationCommandBuilder.BuildAclRepairCommands(
                @"C:\Windows\System32\icacls.exe",
                "D:\\press\"speak",
                ownerSid: AdministratorsSid,
                trustedFullControlSids: [SystemSid],
                readExecuteSid: UserSid));
    }

    [Fact]
    public void BuildAclRepairCommands_ThrowsWhenNoTrustedPrincipalsSpecified()
    {
        Assert.Throws<ArgumentException>(() =>
            StartupRegistrationCommandBuilder.BuildAclRepairCommands(
                @"C:\Windows\System32\icacls.exe",
                @"D:\press-speak",
                ownerSid: AdministratorsSid,
                trustedFullControlSids: [],
                readExecuteSid: UserSid));
    }

    [Fact]
    public void BuildAclRepairElevatedArguments_SurvivesCmdExeFirstAndLastQuoteStrippingRule()
    {
        // cmd.exe 的 /C 有一条文档记录的规则：当其后的命令行以引号开头、且不满足"整行只有
        // 两个引号且引号间是一个可执行文件名"这一狭窄条件时，cmd 会剥离整条命令行里"第一个"
        // 和"最后一个"引号字符（不是按配对剥离）。真机上因为没有套外层引号，这条规则从命令
        // 链中间任意挖走两个引号，彻底打乱配对，产生语法错误（退出代码 1、重定向从未生效）。
        // 这里直接模拟该规则：移除整条字符串里第一个和最后一个引号字符，断言恢复出的内容
        // 正是我们想要的、内部引号完整配对的命令链——如果以后有人误删外层引号包装，这个断言
        // 会先炸，而不是要等到真机上再踩一次同样的坑。
        var commands = StartupRegistrationCommandBuilder.BuildAclRepairCommands(
            @"C:\Windows\System32\icacls.exe",
            @"D:\Program Files\press-speak",
            ownerSid: AdministratorsSid,
            trustedFullControlSids: [SystemSid, AdministratorsSid, TrustedInstallerSid],
            readExecuteSid: UserSid);
        const string logPath = @"C:\Users\ryan\AppData\Local\Temp\press-speak-acl-repair-test.log";

        var arguments = StartupRegistrationCommandBuilder.BuildAclRepairElevatedArguments(commands, logPath);

        Assert.StartsWith("/D /Q /V:OFF /E:ON /C \"", arguments, StringComparison.Ordinal);
        Assert.EndsWith("\"", arguments, StringComparison.Ordinal);

        var afterSwitch = arguments["/D /Q /V:OFF /E:ON /C ".Length..];
        var simulatedCmdExeResult = StripFirstAndLastQuoteCharacter(afterSwitch);

        var expectedChainedCommand =
            $"{commands[0]} > \"{logPath}\" 2>&1 && " +
            $"{commands[1]} >> \"{logPath}\" 2>&1 && " +
            $"{commands[2]} >> \"{logPath}\" 2>&1";
        Assert.Equal(expectedChainedCommand, simulatedCmdExeResult);
    }

    [Fact]
    public void BuildAclRepairElevatedArguments_ThrowsWhenNoCommandsSpecified()
    {
        Assert.Throws<ArgumentException>(() =>
            StartupRegistrationCommandBuilder.BuildAclRepairElevatedArguments(
                [],
                @"C:\Users\ryan\AppData\Local\Temp\log.log"));
    }

    [Fact]
    public void BuildAclRepairElevatedArguments_RejectsCmdUnsafeLogPath()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupRegistrationCommandBuilder.BuildAclRepairElevatedArguments(
                ["\"C:\\Windows\\System32\\icacls.exe\" \"D:\\press-speak\" /inheritance:r"],
                @"C:\Users\ryan\AppData\Local\Temp\log & evil.log"));
    }

    private static string StripFirstAndLastQuoteCharacter(string value)
    {
        var firstQuoteIndex = value.IndexOf('"');
        var lastQuoteIndex = value.LastIndexOf('"');
        Assert.True(firstQuoteIndex >= 0 && lastQuoteIndex > firstQuoteIndex);

        return value.Remove(lastQuoteIndex, 1).Remove(firstQuoteIndex, 1);
    }
}
