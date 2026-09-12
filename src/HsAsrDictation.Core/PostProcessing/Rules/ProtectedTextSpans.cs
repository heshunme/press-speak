using System.Text.RegularExpressions;

namespace HsAsrDictation.PostProcessing.Rules;

/// <summary>Keep recognizable addresses, paths and code intact during prose normalization.</summary>
internal static partial class ProtectedTextSpans
{
    public static IReadOnlyList<(int Start, int Length)> Find(string input) =>
        ProtectedRegex().Matches(input)
            .Select(match => (match.Index, match.Length))
            .ToArray();

    public static bool Overlaps(IReadOnlyList<(int Start, int Length)> spans, int index, int length) =>
        spans.Any(span => index < span.Start + span.Length && span.Start < index + length);

    [GeneratedRegex("""
        `+[^`]*`+
        | "(?:[a-z]:[\\/]|\\\\|~?/|\.\.?/)[^"\r\n]*"
        | '(?:[a-z]:[\\/]|\\\\|~?/|\.\.?/)[^'\r\n]*'
        | [a-z][a-z0-9+.-]*://[^\s<>"'，。；！？、（）【】]+
        | www\.[^\s<>"'，。；！？、（）【】]+
        | [a-z0-9.!\#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+\.[a-z]{2,}
        | (?:[a-z]:[\\/]|\\\\)[^\s<>"'，。；！？、（）【】]+
        | (?<![a-z0-9])(?:~?/|\.\.?/)[\p{L}\p{N}_.-][^\s<>"'，。；！？、（）【】]*
        | [a-z_][a-z0-9_]*_[a-z0-9_]+
        | [a-z_][a-z0-9_]*(?:\.[a-z_][a-z0-9_]*)+
        | [a-z_][a-z0-9_]*(?=[ \t]*\()
        | [a-z][a-z0-9]*[0-9][a-z0-9]*
        | (?<!\S)--?[a-z][a-z0-9-]*
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ProtectedRegex();
}
