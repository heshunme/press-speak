using System.Text.RegularExpressions;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

public sealed class RegexReplaceRule : IPostProcessingRule
{
    private readonly Regex _regex;
    private readonly string _replacement;

    public RegexReplaceRule(
        string id,
        string name,
        int order,
        bool isEnabled,
        string pattern,
        string replacement,
        RegexOptions options = RegexOptions.None)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;
        _replacement = replacement;
        _regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));
    }

    public string Id { get; }

    public string Name { get; }

    public int Order { get; }

    public bool IsEnabled { get; }

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        var output = _regex.Replace(input, _replacement);
        return new RuleApplyResult
        {
            Output = output,
            Changed = !string.Equals(input, output, StringComparison.Ordinal),
            TraceMessage = !string.Equals(input, output, StringComparison.Ordinal)
                ? $"Applied regex '{_regex}'."
                : null
        };
    }
}
