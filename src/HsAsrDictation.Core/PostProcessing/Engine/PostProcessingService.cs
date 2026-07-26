using System.Text.RegularExpressions;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.PostProcessing.Engine;

public sealed class PostProcessingService : IPostProcessingService
{
    private readonly IPostProcessingRuleFactory _factory;
    private readonly LocalLogService _logger;
    private readonly IPostProcessingRuleRepository _repository;

    public PostProcessingService(
        IPostProcessingRuleRepository repository,
        IPostProcessingRuleFactory factory,
        LocalLogService logger)
    {
        _repository = repository;
        _factory = factory;
        _logger = logger;
    }

    public string TryProcess(string input, RuleExecutionContext context)
    {
        return ExecutePipeline(input, context).Output;
    }

    public PostProcessingTraceResult TestProcess(string input, RuleExecutionContext context)
    {
        return ExecutePipeline(_repository.Load(), input, context);
    }

    public PostProcessingTraceResult TestProcess(PostProcessingConfig config, string input, RuleExecutionContext context)
    {
        return ExecutePipeline(config, input, context);
    }

    private PostProcessingTraceResult ExecutePipeline(PostProcessingConfig config, string input, RuleExecutionContext context)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new PostProcessingTraceResult
            {
                Input = input,
                Output = input
            };
        }

        _logger.Info(
            $"后处理开始：enabled={config.IsEnabled} inputLength={input.Length} process={context.ProcessName ?? "<null>"}");

        if (!config.IsEnabled)
        {
            return new PostProcessingTraceResult
            {
                Input = input,
                Output = input
            };
        }

        var traceEntries = new List<PostProcessingTraceEntry>();
        var current = input;

        try
        {
            var rules = config.Rules
                .Select(_factory.Create)
                .Where(rule => rule is not null)
                .Cast<IPostProcessingRule>()
                .OrderBy(rule => rule.Order)
                .ToArray();

            foreach (var rule in rules)
            {
                if (!rule.IsEnabled || !rule.CanApply(context))
                {
                    continue;
                }

                try
                {
                    var result = rule.Apply(current, context);
                    if (result.Changed)
                    {
                        current = result.Output;
                        var message = result.TraceMessage ?? "Rule applied.";
                        traceEntries.Add(new PostProcessingTraceEntry
                        {
                            RuleId = rule.Id,
                            RuleName = rule.Name,
                            Changed = true,
                            Message = message
                        });
                        _logger.Info($"后处理规则命中：{rule.Id} | {message}");
                    }
                }
                catch (RegexMatchTimeoutException ex)
                {
                    traceEntries.Add(new PostProcessingTraceEntry
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Failed = true,
                        Message = $"Regex timeout: {ex.Message}"
                    });
                    _logger.Warn($"后处理规则正则超时：{rule.Id} | {ex.Message}");
                }
                catch (Exception ex)
                {
                    traceEntries.Add(new PostProcessingTraceEntry
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        Failed = true,
                        Message = ex.Message
                    });
                    _logger.Warn($"后处理规则失败：{rule.Id} | {ex.Message}");
                }
            }

            _logger.Info($"后处理结束：outputLength={current.Length} fallback=false");

            return new PostProcessingTraceResult
            {
                Input = input,
                Output = current,
                TraceEntries = traceEntries
            };
        }
        catch (Exception ex)
        {
            _logger.Error("后处理管线失败，已回退原始文本。", ex);
            return new PostProcessingTraceResult
            {
                Input = input,
                Output = input,
                UsedFallback = true,
                TraceEntries = traceEntries
            };
        }
    }

    private PostProcessingTraceResult ExecutePipeline(string input, RuleExecutionContext context) =>
        ExecuteWithRepositoryLoad(input, context);

    private PostProcessingTraceResult ExecuteWithRepositoryLoad(string input, RuleExecutionContext context)
    {
        try
        {
            return ExecutePipeline(_repository.Load(), input, context);
        }
        catch (Exception ex)
        {
            _logger.Error("加载后处理配置失败，已回退原始文本。", ex);
            return new PostProcessingTraceResult
            {
                Input = input,
                Output = input,
                UsedFallback = true
            };
        }
    }
}
