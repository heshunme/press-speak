using System.Text.RegularExpressions;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

public sealed class EnglishCaseNormalizeRule : IPostProcessingRule
{
    public const string DefaultCanonicalTerms = """
        I
        API
        GPT
        CPU
        GPU
        JSON
        HTML
        HTTP
        HTTPS
        SQL
        URL
        USB
        SDK
        CLI
        UI
        ID
        AI
        RAM
        PDF
        XML
        CSS
        TCP
        IP
        DNS
        SSH
        UTF
        UTF-8
        UTF-16
        C#
        C++
        .NET
        ChatGPT
        GitHub
        OpenAI
        iPhone
        JavaScript
        TypeScript
        VS Code
        """;

    private readonly Dictionary<string, string> _canonicalTerms;
    private readonly Regex _tokens;

    public EnglishCaseNormalizeRule(string id, string name, int order, bool isEnabled, string canonicalTerms)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;

        _canonicalTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in canonicalTerms.Split(['\r', '\n', ',', '，'], StringSplitOptions.RemoveEmptyEntries))
        {
            var term = NormalizeTermWhitespace(entry);
            if (term.Length > 0)
            {
                // Keep the first spelling when an entry is repeated with different casing.
                _canonicalTerms.TryAdd(term, term);
            }
        }

        const string wordPattern = @"(?<![A-Za-z0-9_])(?<word>[A-Za-z]+)(?![A-Za-z0-9_])";
        var pattern = wordPattern;
        if (_canonicalTerms.Count > 0)
        {
            var terms = _canonicalTerms.Keys
                .OrderByDescending(term => term.Length)
                .Select(term => string.Join(@"[ \t]+", term.Split(' ').Select(Regex.Escape)));
            pattern = @"(?<![A-Za-z0-9_])(?<term>" + string.Join('|', terms) + @")(?![A-Za-z0-9_])|" + wordPattern;
        }

        _tokens = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
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

        var protectedSpans = ProtectedTextSpans.Find(input);
        var output = _tokens.Replace(input, match =>
        {
            if (ProtectedTextSpans.Overlaps(protectedSpans, match.Index, match.Length))
            {
                return match.Value;
            }

            if (match.Groups["term"].Success)
            {
                return _canonicalTerms.TryGetValue(NormalizeTermWhitespace(match.Value), out var canonicalTerm)
                    ? canonicalTerm
                    : match.Value;
            }

            // Preserve existing mixed case and title case: these may be names or identifiers.
            // A standalone I is valid English even when the custom glossary is empty.
            return match.Value != "I" && match.Value.All(ch => ch is >= 'A' and <= 'Z')
                ? match.Value.ToLowerInvariant()
                : match.Value;
        });

        var changed = !string.Equals(input, output, StringComparison.Ordinal);
        return new RuleApplyResult
        {
            Output = output,
            Changed = changed,
            TraceMessage = changed ? "Normalized English casing and canonical terms." : null
        };
    }

    private static string NormalizeTermWhitespace(string term) =>
        string.Join(' ', term.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
