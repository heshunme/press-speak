using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Rules;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class EnglishAcronymJoinRuleTests
{
    private static readonly RuleExecutionContext Context = new();

    [Theory]
    [InlineData("G P T", "GPT")]
    [InlineData("A P I 文档", "API 文档")]
    [InlineData("G P T 4", "GPT 4")]
    [InlineData("C P U 温度", "CPU 温度")]
    public void Apply_JoinsSpacedAcronyms(string input, string expected)
    {
        var rule = CreateRule();

        var result = rule.Apply(input, Context);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Output);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A 1 B")]
    [InlineData("example@gmail.com")]
    [InlineData("https://a.b.com")]
    [InlineData("A B C D E F G H I")]
    public void Apply_KeepsTextUnchanged_WhenInputShouldNotBeJoined(string input)
    {
        var rule = CreateRule();

        var result = rule.Apply(input, Context);

        Assert.False(result.Changed);
        Assert.Equal(input, result.Output);
    }

    private static EnglishAcronymJoinRule CreateRule() =>
        new("builtin.english-acronym-join", "英语缩写去空格", 400, true, 2, 8);
}
