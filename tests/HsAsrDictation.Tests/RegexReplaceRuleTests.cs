using System.Text.Json.Nodes;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Rules;
using HsAsrDictation.PostProcessing.Validation;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class RegexReplaceRuleTests
{
    [Fact]
    public void Apply_ReplacesMatches_WhenPatternIsValid()
    {
        var rule = new RegexReplaceRule("user.regex", "压缩空格", 100, true, "[ \\t]{2,}", " ");

        var result = rule.Apply("A   B", new RuleExecutionContext());

        Assert.True(result.Changed);
        Assert.Equal("A B", result.Output);
    }

    [Fact]
    public void Validate_ReturnsError_WhenPatternIsInvalid()
    {
        var result = RegexSafetyValidator.Validate("(");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TryProcess_Continues_WhenRegexTimesOut()
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var rulesPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            using var logger = new LocalLogService(logDir);
            var repository = new FakeRepository(new PostProcessingConfig
            {
                IsEnabled = true,
                Rules =
                [
                    new RuleDefinition
                    {
                        Id = "user.timeout",
                        Name = "超时正则",
                        Kind = "regex_replace",
                        IsEnabled = true,
                        Order = 100,
                        Parameters = new JsonObject
                        {
                            ["pattern"] = "(a+)+$",
                            ["replacement"] = "x",
                            ["options"] = "None"
                        }
                    },
                    new RuleDefinition
                    {
                        Id = "user.exact",
                        Name = "精确替换",
                        Kind = "exact_replace",
                        IsEnabled = true,
                        Order = 200,
                        Parameters = new JsonObject
                        {
                            ["find"] = "tail",
                            ["replace"] = "done",
                            ["ignoreCase"] = false
                        }
                    }
                ]
            });

            IPostProcessingService service = new PostProcessingService(
                repository,
                new PostProcessingRuleFactory(logger),
                logger);

            var input = $"{new string('a', 5000)}X tail";
            var output = service.TryProcess(input, new RuleExecutionContext());

            Assert.EndsWith("done", output);
        }
        finally
        {
            TryDeleteFile(rulesPath);
            TryDeleteDirectory(logDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class FakeRepository : IPostProcessingRuleRepository
    {
        private readonly PostProcessingConfig _config;

        public FakeRepository(PostProcessingConfig config)
        {
            _config = config;
        }

        public PostProcessingConfig Load() => _config;

        public void Save(PostProcessingConfig config) => throw new NotSupportedException();

        public void ResetBuiltInOverride(string ruleId) => throw new NotSupportedException();
    }
}
