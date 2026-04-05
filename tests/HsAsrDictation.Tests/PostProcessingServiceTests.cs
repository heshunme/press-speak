using System.Text.Json.Nodes;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class PostProcessingServiceTests
{
    [Fact]
    public void TryProcess_AppliesRulesByOrder()
    {
        using var context = CreateService(new PostProcessingConfig
        {
            IsEnabled = true,
            Rules =
            [
                ExactRule("rule-2", 200, "GPT", "ChatGPT"),
                ExactRule("rule-1", 100, "G P T", "GPT")
            ]
        });

        var output = context.Service.TryProcess("G P T", new RuleExecutionContext());

        Assert.Equal("ChatGPT", output);
    }

    [Fact]
    public void TryProcess_FallsBackToOriginal_WhenRepositoryThrows()
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            IPostProcessingService service = new PostProcessingService(
                new ThrowingRepository(),
                new PostProcessingRuleFactory(logger),
                logger);

            var input = "G P T";
            var output = service.TryProcess(input, new RuleExecutionContext());

            Assert.Equal(input, output);
        }
        finally
        {
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public void TryProcess_SkipsProcessing_WhenSystemDisabled()
    {
        using var context = CreateService(new PostProcessingConfig
        {
            IsEnabled = false,
            Rules = [ExactRule("rule-1", 100, "G P T", "GPT")]
        });

        var output = context.Service.TryProcess("G P T", new RuleExecutionContext());

        Assert.Equal("G P T", output);
    }

    private static RuleDefinition ExactRule(string id, int order, string find, string replace)
    {
        return new RuleDefinition
        {
            Id = id,
            Name = id,
            Kind = "exact_replace",
            IsEnabled = true,
            Order = order,
            Parameters = new JsonObject
            {
                ["find"] = find,
                ["replace"] = replace,
                ["ignoreCase"] = false
            }
        };
    }

    private static ServiceContext CreateService(PostProcessingConfig config)
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logger = new LocalLogService(logDir);
        return new ServiceContext(
            new PostProcessingService(new FakeRepository(config), new PostProcessingRuleFactory(logger), logger),
            logger,
            logDir);
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

    private sealed class ServiceContext : IDisposable
    {
        private readonly LocalLogService _logger;
        private readonly string _logDir;

        public ServiceContext(PostProcessingService service, LocalLogService logger, string logDir)
        {
            Service = service;
            _logger = logger;
            _logDir = logDir;
        }

        public PostProcessingService Service { get; }

        public void Dispose()
        {
            _logger.Dispose();
            TryDeleteDirectory(_logDir);
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

    private sealed class ThrowingRepository : IPostProcessingRuleRepository
    {
        public PostProcessingConfig Load() => throw new InvalidOperationException("boom");

        public void Save(PostProcessingConfig config) => throw new NotSupportedException();

        public void ResetBuiltInOverride(string ruleId) => throw new NotSupportedException();
    }
}
