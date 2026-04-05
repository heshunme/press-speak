using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Engine;

namespace HsAsrDictation.PostProcessing.Abstractions;

public interface IPostProcessingService
{
    string TryProcess(string input, RuleExecutionContext context);

    PostProcessingTraceResult TestProcess(string input, RuleExecutionContext context);

    PostProcessingTraceResult TestProcess(PostProcessingConfig config, string input, RuleExecutionContext context);
}
