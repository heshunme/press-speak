using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Rules;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class ChineseNumberNormalizeRuleTests
{
    private static readonly RuleExecutionContext Context = new();

    [Theory]
    [InlineData("一百二十三个文件", "123个文件")]
    [InlineData("三十二个", "32个")]
    [InlineData("一个文件，每一百个分组", "1个文件，每100个分组")]
    [InlineData("今天三十二个", "今天32个")]
    [InlineData("有两台电脑和九个文件", "有2台电脑和9个文件")]
    [InlineData("十二", "12")]
    [InlineData("一百二十三。", "123。")]
    [InlineData("一千零二个", "1002个")]
    [InlineData("一千零一十件", "1010件")]
    [InlineData("两千三百四十五元", "2345元")]
    [InlineData("一万三千个", "13000个")]
    [InlineData("一万零三件", "10003件")]
    [InlineData("十二万三千四百五十六米", "123456米")]
    [InlineData("一亿零三万件", "100030000件")]
    [InlineData("一亿二千万个", "120000000个")]
    [InlineData("一万亿", "1000000000000")]
    [InlineData("数量是三", "数量是3")]
    [InlineData("金额为一百零二", "金额为102")]
    [InlineData("数值是负三点五", "数值是-3.5")]
    [InlineData("负十二", "-12")]
    [InlineData("正三点五", "+3.5")]
    [InlineData("三点一四一五九", "3.14159")]
    [InlineData("三点五分钟", "3.5分钟")]
    [InlineData("零点零五米", "0.05米")]
    [InlineData("负零点零零", "-0.00")]
    [InlineData("百分之十五", "15%")]
    [InlineData("百分之百", "100%")]
    [InlineData("增长百分之三点五", "增长3.5%")]
    [InlineData("百分之负三", "-3%")]
    [InlineData("负百分之三点五", "-3.5%")]
    [InlineData("百分之15", "15%")]
    [InlineData("编号零零一二", "编号0012")]
    [InlineData("验证码是零一二三四五", "验证码是012345")]
    [InlineData("电话号码：一三八零零一二三四五六", "电话号码：13800123456")]
    [InlineData("数值是3. 14", "数值是3.14")]
    [InlineData("数值：3 \t. 14", "数值：3.14")]
    [InlineData("数值是负3. 14", "数值是-3.14")]
    [InlineData("3 . 14", "3.14")]
    public void Apply_NormalizesExplicitNumbers_AndIsIdempotent(string input, string expected)
    {
        var rule = CreateRule();

        var result = rule.Apply(input, Context);
        var repeated = rule.Apply(result.Output, Context);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Output);
        Assert.False(repeated.Changed);
        Assert.Equal(expected, repeated.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("一起处理，一般情况，万一失败，周一开会，星期三见")]
    [InlineData("三心二意，三言两语，一心一意，一五一十，千方百计")]
    [InlineData("还有一点问题，一点点改进，十分感谢")]
    [InlineData("一次性工具，一条龙服务，一块儿走，三位一体")]
    [InlineData("一本正经，二次元，一天到晚，三天两头")]
    [InlineData("另一个，每一个，同一个，这一个，那一个，哪一个，某一个")]
    [InlineData("上一条，下一项，同一天，前一次")]
    [InlineData("一点一滴，两点一线，三点一线")]
    [InlineData("正三角形，正十二面体，三角函数，一元论")]
    [InlineData("三五分钟")]
    [InlineData("二三十个")]
    [InlineData("一百二个")]
    [InlineData("一万三")]
    [InlineData("一亿二个")]
    [InlineData("一百零二十个")]
    [InlineData("一百百个")]
    [InlineData("十零一个")]
    [InlineData("一千百个")]
    [InlineData("一亿一亿个")]
    [InlineData("几百个，数十个，十几个")]
    [InlineData("数量是一百多，金额是一百来元")]
    [InlineData("一点五十个")]
    [InlineData("二零二六年九月十二日")]
    [InlineData("九月十二号下午三点五分开会")]
    [InlineData("三点半，三点一刻，三时三十分")]
    [InlineData("三点五分三秒，三时五分三秒，下午三点五分三秒")]
    [InlineData("时间是三点五分三秒")]
    [InlineData("下午三点五")]
    [InlineData("时间是三点五，明天三点五见")]
    [InlineData("三分之一，千分之十五，三百分之一")]
    [InlineData("百分之三分之一")]
    [InlineData("三到五个，三至五米")]
    [InlineData("三点五到四点五米")]
    [InlineData("负三到负五，负三点五至正五点六")]
    [InlineData("三万5千个")]
    [InlineData("壹十二个，叁佰元，廿三个")]
    [InlineData("零零一二")]
    [InlineData("值为一二三")]
    [InlineData("值为三五")]
    [InlineData("123个，3.14米，0012，2026-09-12，15%")]
    [InlineData("完成了3. 5个还没完成。")]
    [InlineData("3. 14个文件")]
    [InlineData("3. 14")]
    [InlineData("数值是3.\n14")]
    [InlineData("数值是3 .\r\n14")]
    [InlineData("版本1. 2. 3")]
    [InlineData("数值是1 . 2 . 3")]
    [InlineData("`三十二个`，https://example.com/三十二个")]
    [InlineData("C:\\临时\\三十二个.txt")]
    [InlineData("/tmp/三十二个.txt")]
    [InlineData("item三十二个，三十二API")]
    public void Apply_PreservesAmbiguousUnsupportedAndProtectedExpressions(string input)
    {
        var result = CreateRule().Apply(input, Context);

        Assert.False(result.Changed);
        Assert.Equal(input, result.Output);
    }

    [Fact]
    public void Apply_ProtectsUnsupportedExpressions_WithoutBlockingNearbyQuantities()
    {
        const string input = "周一准备三十二个文件，九月十二日提交，费用百分之十五，三分之一留存。";

        var result = CreateRule().Apply(input, Context);

        Assert.Equal("周一准备32个文件，九月十二日提交，费用15%，三分之一留存。", result.Output);
    }

    [Fact]
    public void Apply_KeepsWholePercentage_WhenPercentageConversionDisabled()
    {
        var rule = new ChineseNumberNormalizeRule("test", "数字", 410, true, convertPercentages: false);

        var result = rule.Apply("增长百分之十五，新增三十二个。", Context);

        Assert.Equal("增长百分之十五，新增32个。", result.Output);
    }

    [Fact]
    public void Apply_StillNormalizesChineseDecimal_WhenDecimalSpacingDisabled()
    {
        var rule = new ChineseNumberNormalizeRule("test", "数字", 410, true, normalizeDecimalSpacing: false);

        var result = rule.Apply("数值是3. 14，另一个是三点五。", Context);

        Assert.Equal("数值是3. 14，另一个是3.5。", result.Output);
    }

    private static ChineseNumberNormalizeRule CreateRule() =>
        new("builtin.chinese-number-normalize", "数字规范化", 410, true);
}
