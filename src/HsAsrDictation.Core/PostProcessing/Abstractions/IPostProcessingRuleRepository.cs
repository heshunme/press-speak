using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.PostProcessing.Abstractions;

public interface IPostProcessingRuleRepository
{
    PostProcessingConfig Load();

    void Save(PostProcessingConfig config);

    void ResetBuiltInOverride(string ruleId);
}
