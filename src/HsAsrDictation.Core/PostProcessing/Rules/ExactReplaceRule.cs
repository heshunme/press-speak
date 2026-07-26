using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

public sealed class ExactReplaceRule : IPostProcessingRule
{
    private readonly string _find;
    private readonly bool _ignoreCase;
    private readonly string _replace;

    public ExactReplaceRule(
        string id,
        string name,
        int order,
        bool isEnabled,
        string find,
        string replace,
        bool ignoreCase)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;
        _find = find;
        _replace = replace;
        _ignoreCase = ignoreCase;
    }

    public string Id { get; }

    public string Name { get; }

    public int Order { get; }

    public bool IsEnabled { get; }

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(_find))
        {
            return new RuleApplyResult { Output = input, Changed = false };
        }

        var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var output = input.Replace(_find, _replace, comparison);

        return new RuleApplyResult
        {
            Output = output,
            Changed = !string.Equals(input, output, StringComparison.Ordinal),
            TraceMessage = !string.Equals(input, output, StringComparison.Ordinal)
                ? $"Replaced '{_find}' with '{_replace}'."
                : null
        };
    }
}
