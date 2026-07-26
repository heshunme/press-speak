using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Abstractions;

public interface IPostProcessingRule
{
    string Id { get; }

    string Name { get; }

    int Order { get; }

    bool IsEnabled { get; }

    bool CanApply(RuleExecutionContext context);

    RuleApplyResult Apply(string input, RuleExecutionContext context);
}
