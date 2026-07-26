using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.PostProcessing.Abstractions;

public interface IPostProcessingRuleFactory
{
    IPostProcessingRule? Create(RuleDefinition definition);
}
