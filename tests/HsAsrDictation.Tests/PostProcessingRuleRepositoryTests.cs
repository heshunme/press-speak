using System.Reflection;
using System.Text.Json;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class PostProcessingRuleRepositoryTests
{
    [Fact]
    public void Load_MergesDefaultRulesAndUserOverrides()
    {
        var repoContext = CreateRepositoryWithUserConfig(new PostProcessingConfig
        {
            IsEnabled = true,
            Rules =
            [
                new RuleDefinition
                {
                    Id = "builtin.english-acronym-join",
                    Name = "英语缩写去空格",
                    Description = "overridden",
                    Kind = "built_in_transform",
                    IsEnabled = false,
                    IsBuiltIn = true,
                    Order = 900,
                    Parameters =
                    {
                        ["transformName"] = "english_acronym_join",
                        ["minLetters"] = 3,
                        ["maxLetters"] = 8
                    }
                },
                new RuleDefinition
                {
                    Id = "user.chatgpt",
                    Name = "ChatGPT 修正",
                    Description = "custom",
                    Kind = "exact_replace",
                    IsEnabled = true,
                    IsBuiltIn = false,
                    Order = 1000,
                    Parameters =
                    {
                        ["find"] = "chat gpt",
                        ["replace"] = "ChatGPT",
                        ["ignoreCase"] = true
                    }
                }
            ]
        });

        try
        {
            var merged = repoContext.Repository.Load();

            Assert.True(merged.IsEnabled);
            Assert.Contains(merged.Rules, rule => rule.Id == "builtin.trim-whitespace");
            var builtIn = Assert.Single(merged.Rules, rule => rule.Id == "builtin.english-acronym-join");
            Assert.False(builtIn.IsEnabled);
            Assert.Equal(900, builtIn.Order);
            Assert.Contains(merged.Rules, rule => rule.Id == "user.chatgpt");
        }
        finally
        {
            repoContext.Dispose();
        }
    }

    [Fact]
    public void ResetBuiltInOverride_RemovesOverrideAndRestoresDefault()
    {
        var repoContext = CreateRepositoryWithUserConfig(new PostProcessingConfig
        {
            IsEnabled = true,
            Rules =
            [
                new RuleDefinition
                {
                    Id = "builtin.english-acronym-join",
                    Name = "英语缩写去空格",
                    Description = "overridden",
                    Kind = "built_in_transform",
                    IsEnabled = false,
                    IsBuiltIn = true,
                    Order = 900,
                    Parameters =
                    {
                        ["transformName"] = "english_acronym_join",
                        ["minLetters"] = 3,
                        ["maxLetters"] = 8
                    }
                }
            ]
        });

        try
        {
            repoContext.Repository.ResetBuiltInOverride("builtin.english-acronym-join");
            var merged = repoContext.Repository.Load();

            var builtIn = Assert.Single(merged.Rules, rule => rule.Id == "builtin.english-acronym-join");
            Assert.True(builtIn.IsEnabled);
            Assert.Equal(400, builtIn.Order);

            var savedUserConfig = JsonSerializer.Deserialize<PostProcessingConfig>(File.ReadAllText(repoContext.UserRulesPath));
            Assert.NotNull(savedUserConfig);
            Assert.DoesNotContain(savedUserConfig!.Rules, rule => rule.Id == "builtin.english-acronym-join");
        }
        finally
        {
            repoContext.Dispose();
        }
    }

    private static RepositoryContext CreateRepositoryWithUserConfig(PostProcessingConfig userConfig)
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var userRulesPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(userRulesPath, JsonSerializer.Serialize(userConfig, new JsonSerializerOptions { WriteIndented = true }));
        var logger = new LocalLogService(logDir);
        return new RepositoryContext(
            new PostProcessingRuleRepository(userRulesPath, logger),
            logger,
            logDir,
            userRulesPath);
    }

    private sealed class RepositoryContext : IDisposable
    {
        private readonly LocalLogService _logger;
        private readonly string _logDir;

        public RepositoryContext(PostProcessingRuleRepository repository, LocalLogService logger, string logDir, string userRulesPath)
        {
            Repository = repository;
            _logger = logger;
            _logDir = logDir;
            UserRulesPath = userRulesPath;
        }

        public PostProcessingRuleRepository Repository { get; }

        public string UserRulesPath { get; }

        public void Dispose()
        {
            _logger.Dispose();

            try
            {
                if (Directory.Exists(_logDir))
                {
                    Directory.Delete(_logDir, recursive: true);
                }
            }
            catch
            {
            }

            try
            {
                if (File.Exists(UserRulesPath))
                {
                    File.Delete(UserRulesPath);
                }
            }
            catch
            {
            }
        }
    }
}
