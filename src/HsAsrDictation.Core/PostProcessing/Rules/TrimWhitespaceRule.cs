using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Rules;

public sealed class TrimWhitespaceRule : IPostProcessingRule
{
    public TrimWhitespaceRule(string id, string name, int order, bool isEnabled)
    {
        Id = id;
        Name = name;
        Order = order;
        IsEnabled = isEnabled;
    }

    public string Id { get; }

    public string Name { get; }

    public int Order { get; }

    public bool IsEnabled { get; }

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        var output = input.Trim();
        return new RuleApplyResult
        {
            Output = output,
            Changed = !string.Equals(input, output, StringComparison.Ordinal),
            TraceMessage = !string.Equals(input, output, StringComparison.Ordinal)
                ? "Trimmed surrounding whitespace."
                : null
        };
    }
}
