using System.Text.Json.Nodes;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Validation;
using HsAsrDictation.Views;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class NormalizationPipelineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"normalization-{Guid.NewGuid():N}");
    private readonly LocalLogService _logger;
    private readonly PostProcessingRuleRepository _repository;
    private readonly PostProcessingService _service;

    public NormalizationPipelineTests()
    {
        _logger = new LocalLogService(_directory);
        _repository = new PostProcessingRuleRepository(Path.Combine(_directory, "rules.json"), _logger);
        _service = new PostProcessingService(_repository, new PostProcessingRuleFactory(_logger), _logger);
    }

    [Theory]
    [InlineData("  HELLO，G P T 有三十二个 A P I  ", "hello，GPT 有32个 API")]
    [InlineData("数值是负三点五，增长百分之十五，编号零零一二", "数值是-3.5，增长15%，编号0012")]
    [InlineData("一起在周一用 GitHub 讨论三五分钟", "一起在周一用 GitHub 讨论三五分钟")]
    [InlineData("HELLO https://EXAMPLE.com/ABC/三十二个，编号零零一二", "hello https://EXAMPLE.com/ABC/三十二个，编号0012")]
    [InlineData("HELLO /临时/三十二个 WORLD", "hello /临时/三十二个 world")]
    [InlineData("HELLO \"C:\\Program Files\\MY APP\\三十二个\" WORLD", "hello \"C:\\Program Files\\MY APP\\三十二个\" world")]
    public void DefaultPipeline_NormalizesFinalTextAndIsIdempotent(string input, string expected)
    {
        var result = _service.TestProcess(input, new RuleExecutionContext());

        Assert.False(result.UsedFallback);
        Assert.DoesNotContain(result.TraceEntries, entry => entry.Failed);
        Assert.Equal(expected, result.Output);
        Assert.Equal(expected, _service.TryProcess(result.Output, new RuleExecutionContext()));
    }

    [Theory]
    [InlineData("builtin.chinese-number-normalize", "hello 三十二个 API")]
    [InlineData("builtin.english-case-normalize", "HELLO 32个 API")]
    public void Rules_CanBeIndependentlyDisabledAndSaved(string id, string expected)
    {
        var config = _repository.Load();
        config.Rules.Single(rule => rule.Id == id).IsEnabled = false;
        _repository.Save(config);

        Assert.Equal(expected, _service.TryProcess("HELLO 三十二个 A P I", new RuleExecutionContext()));
        Assert.False(_repository.Load().Rules.Single(rule => rule.Id == id).IsEnabled);
        _repository.ResetBuiltInOverride(id);
        Assert.Equal("hello 32个 API", _service.TryProcess("HELLO 三十二个 A P I", new RuleExecutionContext()));
    }

    [Fact]
    public void OldConfiguration_KeepsUserReplacementAfterNewDefaultsAndPreservesSwitch()
    {
        var oldConfig = new PostProcessingConfig
        {
            IsEnabled = false,
            Rules = [Replacement("user.term", 500, "hello", "HelloProject")]
        };
        _repository.Save(oldConfig);
        var loaded = _repository.Load();

        Assert.False(loaded.IsEnabled);
        Assert.Equal("HELLO", _service.TryProcess("HELLO", new RuleExecutionContext()));
        Assert.Equal(500, loaded.Rules.Single(rule => rule.Id == "user.term").Order);
        Assert.True(loaded.Rules.Single(rule => rule.Id == "builtin.english-case-normalize").Order < 500);

        loaded.IsEnabled = true;
        // The settings editor renumbers the list when saving: order must still survive this round trip.
        _repository.Save(new PostProcessingRulesViewModel(loaded).BuildConfig());
        Assert.Equal("HelloProject", _service.TryProcess("HELLO", new RuleExecutionContext()));
    }

    [Theory]
    [InlineData("chinese_number_normalize", "convertPercentages")]
    [InlineData("english_case_normalize", "canonicalTerms")]
    public void InvalidParameter_IsRejectedWithoutDiscardingOtherRules(string transform, string parameter)
    {
        var invalid = new RuleDefinition
        {
            Id = "invalid",
            Name = "invalid",
            Kind = "built_in_transform",
            Order = 200,
            Parameters = new JsonObject { ["transformName"] = transform, [parameter] = 123 }
        };
        Assert.False(RuleValidator.ValidateRule(invalid).Ok);
        var config = new PostProcessingConfig
        {
            Rules = [Replacement("before", 100, "before", "middle"), invalid, Replacement("after", 300, "middle", "after")]
        };

        var result = _service.TestProcess(config, "before", new RuleExecutionContext());

        Assert.False(result.UsedFallback);
        Assert.Equal("after", result.Output);
    }

    private static RuleDefinition Replacement(string id, int order, string find, string replacement) => new()
    {
        Id = id,
        Name = id,
        Kind = "exact_replace",
        Order = order,
        Parameters = new JsonObject { ["find"] = find, ["replace"] = replacement }
    };

    public void Dispose()
    {
        _logger.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
