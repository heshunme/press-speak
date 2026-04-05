using System.Text.RegularExpressions;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

public sealed partial class EnglishAcronymJoinRule : IPostProcessingRule
{
    private readonly int _maxLetters;
    private readonly int _minLetters;

    public EnglishAcronymJoinRule(string id, string name, int order, bool isEnabled, int minLetters, int maxLetters)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;
        _minLetters = minLetters;
        _maxLetters = maxLetters;
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
            return new RuleApplyResult { Output = input, Changed = false };
        }

        var changed = false;
        var output = AcronymRegex().Replace(input, match =>
        {
            var letters = 0;

            foreach (var ch in match.Value)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    if (!IsAsciiLetter(ch))
                    {
                        return match.Value;
                    }

                    letters++;
                }
            }

            if (letters < _minLetters || letters > _maxLetters)
            {
                return match.Value;
            }

            if (IsPartOfLongerSequence(input, match.Index, match.Length))
            {
                return match.Value;
            }

            if (LooksLikeUrlOrEmailContext(input, match.Index, match.Length))
            {
                return match.Value;
            }

            changed = true;
            return RemoveWhitespace(match.Value);
        });

        return new RuleApplyResult
        {
            Output = output,
            Changed = changed,
            TraceMessage = changed ? "Joined spaced English acronym." : null
        };
    }

    private static bool IsAsciiLetter(char ch) =>
        (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

    private static string RemoveWhitespace(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;

        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                buffer[count++] = ch;
            }
        }

        return new string(buffer[..count]);
    }

    private static bool LooksLikeUrlOrEmailContext(string input, int index, int length)
    {
        var start = Math.Max(0, index - 16);
        var end = Math.Min(input.Length, index + length + 16);
        var slice = input[start..end];

        return slice.Contains("://", StringComparison.Ordinal) ||
               slice.Contains('@', StringComparison.Ordinal);
    }

    private static bool IsPartOfLongerSequence(string input, int index, int length)
    {
        var before = index - 2;
        if (before >= 0 &&
            char.IsWhiteSpace(input[index - 1]) &&
            IsAsciiLetter(input[before]))
        {
            return true;
        }

        var after = index + length;
        if (after + 1 < input.Length &&
            char.IsWhiteSpace(input[after]) &&
            IsAsciiLetter(input[after + 1]))
        {
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"(?<![A-Za-z])(?:[A-Za-z](?:[ \t]+[A-Za-z]){1,7})(?![A-Za-z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AcronymRegex();
}
