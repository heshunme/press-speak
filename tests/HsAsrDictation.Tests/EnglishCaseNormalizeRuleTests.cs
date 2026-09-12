using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Rules;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class EnglishCaseNormalizeRuleTests
{
    private static readonly RuleExecutionContext Context = new();

    [Theory]
    [InlineData("HELLO WORLD", "hello world")]
    [InlineData("请用 ENGLISH 回答", "请用 english 回答")]
    [InlineData("IN ON IT AS AN AT TO A", "in on it as an at to a")]
    [InlineData("I AM HERE", "I am here")]
    [InlineData("DON'T CHANGE THIS", "don't change this")]
    [InlineData("HELLO-WORLD", "hello-world")]
    [InlineData("调用api获取json", "调用API获取JSON")]
    [InlineData("CHATGPT GITHUB OPENAI IPHONE", "ChatGPT GitHub OpenAI iPhone")]
    [InlineData("使用 c++、c# 和 .net", "使用 C++、C# 和 .NET")]
    [InlineData("HELLO c# WORLD .net", "hello C# world .NET")]
    [InlineData("打开 vs   code", "打开 VS Code")]
    [InlineData("vs\tcode", "VS Code")]
    [InlineData("VS\nCODE", "vs\ncode")]
    [InlineData("VS\r\nCODE", "vs\r\ncode")]
    [InlineData("APIARY RAPID GITHUBBER", "apiary rapid githubber")]
    public void Apply_NormalizesUppercaseWordsAndCanonicalTerms(string input, string expected)
    {
        var result = CreateRule().Apply(input, Context);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("你好，世界。")]
    [InlineData("hello world")]
    [InlineData("Hello John GitHub iPhone myVariable")]
    [InlineData("API GPT CPU JSON I")]
    [InlineData("UTF-8 UTF-16 C# C++ .NET VS Code")]
    [InlineData("API2 2API API_VALUE _HELLO HELLO_ H264")]
    public void Apply_PreservesExistingCasingAndTokenBoundaries(string input)
    {
        var result = CreateRule().Apply(input, Context);

        Assert.False(result.Changed);
        Assert.Null(result.TraceMessage);
        Assert.Equal(input, result.Output);
    }

    [Theory]
    [InlineData("https://EXAMPLE.COM/HELLO?API=JSON")]
    [InlineData("HELLO@EXAMPLE.COM")]
    [InlineData(@"C:\Users\HELLO\README.TXT")]
    [InlineData("/home/HELLO/README.TXT")]
    [InlineData("`HELLO API GITHUB`")]
    [InlineData("```\nHELLO API GITHUB\n```")]
    [InlineData("USER_VALUE")]
    [InlineData("GET()")]
    [InlineData("GITHUB()")]
    [InlineData("API.Value")]
    public void Apply_PreservesProtectedText(string protectedText)
    {
        var result = CreateRule().Apply("BEFORE " + protectedText + " AFTER", Context);

        Assert.Equal("before " + protectedText + " after", result.Output);
    }

    [Fact]
    public void Apply_UsesEditableGlossaryWithLongestPhraseFirst()
    {
        var rule = CreateRule("Code\r\n  VS   Code , MyProduct，Acme AI\nAPI\napi");

        var result = rule.Apply("VS CODE MYPRODUCT ACME AI CODE API GITHUB", Context);

        Assert.Equal("VS Code MyProduct Acme AI Code API github", result.Output);
    }

    [Fact]
    public void Apply_EscapesGlossaryPunctuation()
    {
        var rule = CreateRule("C++, C#, .NET");

        var result = rule.Apply("c++ c# .net XNET", Context);

        Assert.Equal("C++ C# .NET xnet", result.Output);
    }

    [Fact]
    public void Apply_DoesNotMatchGlossarySubstringsInsideLargerTokens()
    {
        var rule = CreateRule("Api, My Product");

        var result = rule.Apply("APIARY API2 API_VALUE MY PRODUCTS MY PRODUCTIVITY MY PRODUCT", Context);

        Assert.Equal("apiary API2 API_VALUE my products my productivity My Product", result.Output);
    }

    [Fact]
    public void Apply_AllowsEmptyGlossary()
    {
        var result = CreateRule(string.Empty).Apply("API GPT CHATGPT I", Context);

        Assert.Equal("api gpt chatgpt I", result.Output);
    }

    [Fact]
    public void Apply_WorksAfterAcronymJoining()
    {
        var acronymRule = new EnglishAcronymJoinRule("acronym", "Acronyms", 400, true, 2, 8);
        var joined = acronymRule.Apply("使用 G P T 和 A P I 处理 HELLO WORLD", Context).Output;

        var result = CreateRule().Apply(joined, Context);

        Assert.Equal("使用 GPT 和 API 处理 hello world", result.Output);
    }

    [Theory]
    [InlineData("HELLO WORLD API GITHUB VS CODE I")]
    [InlineData("调用api与ChatGPT和iPhone处理JSON")]
    [InlineData("HELLO https://EXAMPLE.COM/API `GITHUB` WORLD")]
    public void Apply_IsIdempotent(string input)
    {
        var rule = CreateRule();
        var first = rule.Apply(input, Context);

        var second = rule.Apply(first.Output, Context);

        Assert.Equal(first.Output, second.Output);
        Assert.False(second.Changed);
    }

    private static EnglishCaseNormalizeRule CreateRule(string? canonicalTerms = null) =>
        new("builtin.english-case-normalize", "英文大小写规范化", 600, true,
            canonicalTerms ?? EnglishCaseNormalizeRule.DefaultCanonicalTerms);
}
