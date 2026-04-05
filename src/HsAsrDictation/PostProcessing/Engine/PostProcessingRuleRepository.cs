using System.Reflection;
using System.Text.Json;
using System.IO;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.PostProcessing.Engine;

public sealed class PostProcessingRuleRepository : IPostProcessingRuleRepository
{
    private const string DefaultResourceName = "HsAsrDictation.Resources.PostProcessing.default-rules.json";

    private readonly LocalLogService _logger;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _userRulesPath;

    public PostProcessingRuleRepository(string userRulesPath, LocalLogService logger)
    {
        _userRulesPath = userRulesPath;
        _logger = logger;
    }

    public PostProcessingConfig Load()
    {
        var defaults = LoadDefaultConfig();
        var user = LoadUserConfig(defaults.IsEnabled);
        var merged = Merge(defaults, user);
        _logger.Info($"后处理规则已加载：enabled={merged.IsEnabled} total={merged.Rules.Count}");
        return merged;
    }

    public void Save(PostProcessingConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userRulesPath)!);
        var defaults = LoadDefaultConfig();
        var userConfig = BuildUserConfig(config, defaults);
        File.WriteAllText(_userRulesPath, JsonSerializer.Serialize(userConfig, _serializerOptions));
        _logger.Info($"后处理用户规则已保存：{_userRulesPath}");
    }

    public void ResetBuiltInOverride(string ruleId)
    {
        var defaults = LoadDefaultConfig();
        var user = LoadUserConfig(defaults.IsEnabled);
        user.Rules.RemoveAll(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal));

        var merged = Merge(defaults, user);
        Save(merged);
    }

    private PostProcessingConfig LoadDefaultConfig()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(DefaultResourceName) ??
                     FindDefaultResourceStream(assembly);
        if (stream is null)
        {
            throw new InvalidOperationException($"找不到默认后处理规则资源：{DefaultResourceName}");
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<PostProcessingConfig>(json, _serializerOptions) ?? new PostProcessingConfig();
        }
    }

    private static Stream? FindDefaultResourceStream(Assembly assembly)
    {
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default-rules.json", StringComparison.Ordinal));

        return resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private PostProcessingConfig LoadUserConfig(bool defaultIsEnabled)
    {
        if (!File.Exists(_userRulesPath))
        {
            return new PostProcessingConfig
            {
                IsEnabled = defaultIsEnabled
            };
        }

        try
        {
            var json = File.ReadAllText(_userRulesPath);
            return JsonSerializer.Deserialize<PostProcessingConfig>(json, _serializerOptions) ?? new PostProcessingConfig
            {
                IsEnabled = defaultIsEnabled
            };
        }
        catch (Exception ex)
        {
            _logger.Error("加载用户后处理规则失败，已回退默认规则。", ex);
            return new PostProcessingConfig
            {
                IsEnabled = defaultIsEnabled
            };
        }
    }

    private static PostProcessingConfig Merge(PostProcessingConfig defaults, PostProcessingConfig user)
    {
        var mergedRules = defaults.Rules
            .Select(CloneRule)
            .ToDictionary(rule => rule.Id, StringComparer.Ordinal);

        foreach (var userRule in user.Rules.Select(CloneRule))
        {
            mergedRules[userRule.Id] = userRule;
        }

        return new PostProcessingConfig
        {
            Version = Math.Max(defaults.Version, user.Version),
            IsEnabled = user.IsEnabled,
            Rules = mergedRules.Values.OrderBy(rule => rule.Order).ThenBy(rule => rule.Id, StringComparer.Ordinal).ToList()
        };
    }

    private static PostProcessingConfig BuildUserConfig(PostProcessingConfig merged, PostProcessingConfig defaults)
    {
        var defaultsById = defaults.Rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        var userRules = new List<RuleDefinition>();

        foreach (var rule in merged.Rules)
        {
            if (!rule.IsBuiltIn)
            {
                userRules.Add(CloneRule(rule));
                continue;
            }

            if (!defaultsById.TryGetValue(rule.Id, out var defaultRule) ||
                !AreRulesEquivalent(rule, defaultRule))
            {
                userRules.Add(CloneRule(rule));
            }
        }

        return new PostProcessingConfig
        {
            Version = merged.Version,
            IsEnabled = merged.IsEnabled,
            Rules = userRules
        };
    }

    private static RuleDefinition CloneRule(RuleDefinition rule)
    {
        return new RuleDefinition
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            Kind = rule.Kind,
            IsEnabled = rule.IsEnabled,
            IsBuiltIn = rule.IsBuiltIn,
            Order = rule.Order,
            Parameters = rule.Parameters.DeepClone().AsObject()
        };
    }

    private static bool AreRulesEquivalent(RuleDefinition left, RuleDefinition right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
               string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) &&
               left.IsEnabled == right.IsEnabled &&
               left.IsBuiltIn == right.IsBuiltIn &&
               left.Order == right.Order &&
               string.Equals(left.Parameters.ToJsonString(), right.Parameters.ToJsonString(), StringComparison.Ordinal);
    }
}
