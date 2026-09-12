using System.Globalization;
using System.Text.RegularExpressions;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

/// <summary>
/// Converts explicit quantities while leaving incomplete, idiomatic and unsupported expressions alone.
/// This deliberately does not attempt to infer dates, times or omitted Chinese place values.
/// </summary>
public sealed partial class ChineseNumberNormalizeRule : IPostProcessingRule
{
    private readonly bool _convertPercentages;
    private readonly bool _normalizeDecimalSpacing;

    public ChineseNumberNormalizeRule(
        string id,
        string name,
        int order,
        bool isEnabled,
        bool convertPercentages = true,
        bool normalizeDecimalSpacing = true)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;
        _convertPercentages = convertPercentages;
        _normalizeDecimalSpacing = normalizeDecimalSpacing;
    }

    public string Id { get; }

    public string Name { get; }

    public int Order { get; }

    public bool IsEnabled { get; }

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new RuleApplyResult { Output = input };
        }

        var protectedSpans = FindProtectedSpans(input);
        var output = NumberRegex().Replace(input, match =>
        {
            if (ProtectedTextSpans.Overlaps(protectedSpans, match.Index, match.Length) ||
                HasUnsupportedNeighbor(input, match.Index, match.Length))
            {
                return match.Value;
            }

            var identifier = HasPrefix(IdentifierPrefixRegex(), input, match.Index);
            if (!TryNormalize(match.Value, identifier, out var normalized, out var explicitNumber))
            {
                return match.Value;
            }

            if (!explicitNumber && !identifier &&
                !HasPrefix(NumericPrefixRegex(), input, match.Index) &&
                !HasQuantitySuffix(input, match.Index + match.Length) &&
                !IsWholeExpression(input, match.Index, match.Length))
            {
                return match.Value;
            }

            return normalized;
        });

        if (_normalizeDecimalSpacing)
        {
            // The earlier replacements change offsets, so find spans in the new text again.
            var decimalSpans = FindProtectedSpans(output);
            var decimalInput = output;
            output = SpacedDecimalRegex().Replace(decimalInput, match =>
            {
                if (ProtectedTextSpans.Overlaps(decimalSpans, match.Index, match.Length) ||
                    HasUnsupportedNeighbor(decimalInput, match.Index, match.Length))
                {
                    return match.Value;
                }

                // A dot followed by a space can be a sentence or list marker. Only an explicit
                // value label, or a standalone expression with whitespace before the dot, opts in.
                if (!HasPrefix(NumericPrefixRegex(), decimalInput, match.Index) &&
                    !(match.Groups["before"].Length > 0 &&
                      IsWholeExpression(decimalInput, match.Index, match.Length)))
                {
                    return match.Value;
                }

                return match.Groups["integer"].Value + "." + match.Groups["fraction"].Value;
            });
        }

        var changed = !string.Equals(input, output, StringComparison.Ordinal);
        return new RuleApplyResult
        {
            Output = output,
            Changed = changed,
            TraceMessage = changed ? "Normalized explicit Chinese numbers." : null
        };
    }

    private bool TryNormalize(string value, bool identifier, out string normalized, out bool explicitNumber)
    {
        normalized = value;
        explicitNumber = false;
        var sign = string.Empty;
        if (value[0] is '负' or '正')
        {
            sign = value[0] == '负' ? "-" : "+";
            value = value[1..];
        }

        var percentage = value.StartsWith("百分之", StringComparison.Ordinal);
        if (percentage)
        {
            if (!_convertPercentages)
            {
                return false;
            }

            value = value[3..];
            if (value.Length > 0 && value[0] is '负' or '正')
            {
                if (sign.Length > 0)
                {
                    return false;
                }

                sign = value[0] == '负' ? "-" : "+";
                value = value[1..];
            }
        }

        if (value.Length == 0)
        {
            return false;
        }

        var dot = value.IndexOfAny(['点', '.']);
        var integerText = dot < 0 ? value : value[..dot];
        var fractionText = dot < 0 ? null : value[(dot + 1)..];
        if (integerText.Length == 0 ||
            (fractionText is not null &&
             (fractionText.Length == 0 || !fractionText.All(IsDigit))))
        {
            return false;
        }

        string integer;
        if (integerText.All(char.IsAsciiDigit))
        {
            integer = integerText;
        }
        else if (percentage && integerText == "百")
        {
            integer = "100";
        }
        else if (integerText.Any(char.IsAsciiDigit))
        {
            // Mixed Arabic and Chinese place values need a separate grammar.
            return false;
        }
        else if (integerText.All(IsDigit))
        {
            if (integerText.Length > 1 && !identifier)
            {
                return false;
            }

            integer = ConvertDigits(integerText);
        }
        else
        {
            if (!TryParseInteger(integerText, out var number))
            {
                return false;
            }

            integer = number.ToString(CultureInfo.InvariantCulture);
        }

        normalized = sign + integer + (fractionText is null ? string.Empty : "." + ConvertDigits(fractionText)) +
                     (percentage ? "%" : string.Empty);
        explicitNumber = percentage || sign.Length > 0 || dot >= 0;
        return true;
    }

    private static bool TryParseInteger(string value, out long result)
    {
        result = 0;
        var yi = value.IndexOf('亿');
        if (yi < 0)
        {
            return TryParseBelowYi(value, out result);
        }

        if (yi == 0 || value.LastIndexOf('亿') != yi ||
            !TryParseBelowYi(value[..yi], out var upper) || upper == 0)
        {
            return false;
        }

        var lowerText = value[(yi + 1)..];
        if (!TryParseLowerSection(lowerText, TryParseBelowYi, out var lower))
        {
            return false;
        }

        result = upper * 100_000_000L + lower;
        return true;
    }

    private static bool TryParseBelowYi(string value, out long result)
    {
        result = 0;
        var wan = value.IndexOf('万');
        if (wan < 0)
        {
            return TryParseBelowWan(value, out result);
        }

        if (wan == 0 || value.LastIndexOf('万') != wan ||
            !TryParseBelowWan(value[..wan], out var upper) || upper == 0)
        {
            return false;
        }

        var lowerText = value[(wan + 1)..];
        if (!TryParseLowerSection(lowerText, TryParseBelowWan, out var lower))
        {
            return false;
        }

        result = upper * 10_000L + lower;
        return true;
    }

    private delegate bool SectionParser(string value, out long result);

    private static bool TryParseLowerSection(string value, SectionParser parser, out long result)
    {
        result = 0;
        if (value.Length == 0)
        {
            return true;
        }

        if (value[0] is '零' or '〇')
        {
            return value.Length > 1 && parser(value[1..], out result) && result > 0;
        }

        // 一万二 / 一亿三 omit a place value and are ambiguous.
        return value.Length > 1 && parser(value, out result) && result > 0;
    }

    private static bool TryParseBelowWan(string value, out long result)
    {
        result = 0;
        if (value.Length == 0)
        {
            return false;
        }

        var lastUnit = 10_000;
        var pendingDigit = -1;
        var hasZero = false;
        for (var index = 0; index < value.Length; index++)
        {
            var digit = DigitValue(value[index]);
            if (digit >= 0)
            {
                if (digit == 0)
                {
                    if (index == 0 || pendingDigit >= 0 || hasZero || lastUnit <= 10 || index == value.Length - 1)
                    {
                        return false;
                    }

                    hasZero = true;
                }
                else
                {
                    if (pendingDigit >= 0)
                    {
                        return false;
                    }

                    pendingDigit = digit;
                }

                continue;
            }

            var unit = value[index] switch { '十' => 10, '百' => 100, '千' => 1_000, _ => 0 };
            if (unit == 0 || unit >= lastUnit ||
                (pendingDigit < 0 && !(index == 0 && unit == 10)) ||
                (hasZero && unit >= lastUnit / 10))
            {
                return false;
            }

            result += (pendingDigit < 0 ? 1 : pendingDigit) * unit;
            pendingDigit = -1;
            hasZero = false;
            lastUnit = unit;
        }

        if (pendingDigit >= 0)
        {
            if (lastUnit > 10 && lastUnit < 10_000 && !hasZero)
            {
                return false;
            }

            result += pendingDigit;
        }

        return result > 0;
    }

    private static List<(int Start, int Length)> FindProtectedSpans(string input)
    {
        var spans = ProtectedTextSpans.Find(input).ToList();
        foreach (Match match in UnsupportedExpressionRegex().Matches(input))
        {
            spans.Add((match.Index, match.Length));
        }

        foreach (Match match in FractionRegex().Matches(input))
        {
            if (!match.Value.StartsWith("百分之", StringComparison.Ordinal) ||
                match.Value.IndexOf("分之", 3, StringComparison.Ordinal) >= 0)
            {
                spans.Add((match.Index, match.Length));
            }
        }

        return spans;
    }

    private static bool HasUnsupportedNeighbor(string input, int index, int length)
    {
        var end = index + length;
        return (index > 0 && (input[index - 1] is '几' or '数' or '第' || IsIdentifierCharacter(input[index - 1]))) ||
               (end < input.Length && (input[end] is '几' or '多' or '余' or '来' or '半' ||
                                       IsIdentifierCharacter(input[end])));
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '`';

    private static bool HasPrefix(Regex regex, string input, int index) =>
        regex.IsMatch(input[Math.Max(0, index - 24)..index]);

    private static bool HasQuantitySuffix(string input, int index) =>
        QuantitySuffixRegex().IsMatch(input[index..]);

    private static bool IsWholeExpression(string input, int index, int length) =>
        input.AsSpan(0, index).Trim().IsEmpty &&
        input.AsSpan(index + length).Trim().TrimEnd("。！？.!?").IsEmpty;

    private static bool IsDigit(char value) => DigitValue(value) >= 0;

    private static int DigitValue(char value) => value switch
    {
        '零' or '〇' or '0' => 0,
        '一' or '1' => 1,
        '二' or '两' or '2' => 2,
        '三' or '3' => 3,
        '四' or '4' => 4,
        '五' or '5' => 5,
        '六' or '6' => 6,
        '七' or '7' => 7,
        '八' or '8' => 8,
        '九' or '9' => 9,
        _ => -1
    };

    private static string ConvertDigits(string value) =>
        new(value.Select(character => (char)('0' + DigitValue(character))).ToArray());

    // Include unsupported financial digits/units in the candidate to avoid converting just its tail.
    [GeneratedRegex(@"[负正]?(?:百分之)?[负正]?[0-9零〇一二两三四五六七八九十百千万亿壹贰叁肆伍陆柒捌玖拾佰仟萬億兆廿卅点]+(?:\.[0-9零〇一二两三四五六七八九十百千万亿点]+)*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[0-9零〇一二两三四五六七八九十百千万亿]+(?:分之[负正]?[0-9零〇一二两三四五六七八九十百千万亿点]+)+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FractionRegex();

    [GeneratedRegex("""
        [0-9零〇一二两三四五六七八九十百千万亿]+(?:年|月|日|号)
        | [0-9]+(?:[ \t]*\.[ \t]*[0-9]+){2,}
        | (?:(?:上午|下午|早上|晚上|凌晨|中午|今天|明天|昨天|今晚|明早)[ \t]*
             | (?:时间|时刻)(?:是|为|：|:|[ \t])*)?
          [0-9零〇一二两三四五六七八九十百千万亿]+点
          (?:[0-9零〇一二两三四五六七八九十百千万亿]+分(?!钟)(?:[0-9零〇一二两三四五六七八九十百千万亿]+秒)?
             | [0-9零〇一二两三四五六七八九十百千万亿]+秒
             | (?:半|[一二三四]刻)(?:[0-9零〇一二两三四五六七八九十百千万亿]+秒)?)
        | (?:(?:上午|下午|早上|晚上|凌晨|中午|今天|明天|昨天|今晚|明早)[ \t]*
             | (?:时间|时刻)(?:是|为|：|:|[ \t])*)
          [0-9零〇一二两三四五六七八九十百千万亿]+点[0-9零〇一二两三四五六七八九十百千万亿]*
        | [0-9零〇一二两三四五六七八九十百千万亿]+时[0-9零〇一二两三四五六七八九十百千万亿]+分
          (?:[0-9零〇一二两三四五六七八九十百千万亿]+秒)?
        | [负正]?[0-9零〇一二两三四五六七八九十百千万亿点]+(?:到|至|或|[-~～])[负正]?[0-9零〇一二两三四五六七八九十百千万亿点]+
        | (?:星期|礼拜|周)[一二三四五六七日天]
        | (?:另|每|同|这|那|哪|某|上|下|前|后)一(?:个|位|种|项|次|天|条|本|件|份|组|套|张|台)
        | 万一|一五一十|十分(?:感谢|感激|抱歉|高兴|满意|重要|认真|清楚)
        | 百发百中|千方百计|三心二意|三言两语|一心一意|一本正经|一次性|二次元|一条龙|一块儿|三位一体|一天到晚|三天两头|一点一滴|[两三]点一线
        | 正?[一二两三四五六七八九十百千万亿]+(?:角形|面体|角函数)
        """, RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace, matchTimeoutMilliseconds: 100)]
    private static partial Regex UnsupportedExpressionRegex();

    [GeneratedRegex(@"(?:编号|号码|手机号|电话号码|电话|邮编|验证码|序号|工号|学号|账号|订单号|门牌号|密码)(?:是|为|：|:|[ \t])*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IdentifierPrefixRegex();

    [GeneratedRegex(@"(?:数值|数字|结果|金额|价格|温度|重量|长度|面积|比例|速度|余额|总计|合计|数量|值|等于)(?:是|为|约|等于|：|:|[ \t])*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumericPrefixRegex();

    [GeneratedRegex(@"^[ \t]*(?:个|位|件|台|本|张|页|份|条|项|次|种|组|套|块|元(?!论|化)|角钱|厘米|毫米|公里|千米|米|公斤|千克|毫克|斤|克|吨|毫升|升|秒|分钟|小时|天|倍|岁)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex QuantitySuffixRegex();

    [GeneratedRegex(@"(?<![0-9.])(?<integer>[-+]?[0-9]+)(?<before>[ \t]*)\.[ \t]*(?<fraction>[0-9]+)(?![0-9.])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SpacedDecimalRegex();
}
