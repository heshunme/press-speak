using System.Text.Json.Nodes;
using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.PostProcessing.Validation;

public static class RuleValidator
{
    public static (bool Ok, string? Error) ValidateConfig(PostProcessingConfig config)
    {
        foreach (var rule in config.Rules)
        {
            var (ok, error) = ValidateRule(rule);
            if (!ok)
            {
                return (false, error);
            }
        }

        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateRule(RuleDefinition rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            return (false, "规则 ID 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            return (false, $"规则 {rule.Id} 的名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(rule.Kind))
        {
            return (false, $"规则 {rule.Id} 的类型不能为空。");
        }

        if (rule.Order < 0)
        {
            return (false, $"规则 {rule.Id} 的顺序不能小于 0。");
        }

        return rule.Kind switch
        {
            "exact_replace" => ValidateExactReplace(rule.Id, rule.Parameters),
            "regex_replace" => ValidateRegexReplace(rule.Id, rule.Parameters),
            "built_in_transform" => ValidateBuiltIn(rule.Id, rule.Parameters),
            _ => (false, $"规则 {rule.Id} 的类型不受支持：{rule.Kind}")
        };
    }

    private static (bool Ok, string? Error) ValidateExactReplace(string ruleId, JsonObject parameters)
    {
        if (GetString(parameters, "find") is null)
        {
            return (false, $"规则 {ruleId} 缺少 find 参数。");
        }

        if (GetString(parameters, "replace") is null)
        {
            return (false, $"规则 {ruleId} 缺少 replace 参数。");
        }

        return (true, null);
    }

    private static (bool Ok, string? Error) ValidateRegexReplace(string ruleId, JsonObject parameters)
    {
        var pattern = GetString(parameters, "pattern");
        if (pattern is null)
        {
            return (false, $"规则 {ruleId} 缺少 pattern 参数。");
        }

        var replacement = GetString(parameters, "replacement");
        if (replacement is null)
        {
            return (false, $"规则 {ruleId} 缺少 replacement 参数。");
        }

        var result = RegexSafetyValidator.Validate(pattern, GetString(parameters, "options"));
        return result.Ok ? (true, null) : (false, $"规则 {ruleId} 的正则无效：{result.Error}");
    }

    private static (bool Ok, string? Error) ValidateBuiltIn(string ruleId, JsonObject parameters)
    {
        var transformName = GetString(parameters, "transformName");
        if (string.IsNullOrWhiteSpace(transformName))
        {
            return (false, $"规则 {ruleId} 缺少 transformName 参数。");
        }

        if (string.Equals(transformName, "english_acronym_join", StringComparison.Ordinal))
        {
            var minLetters = GetInt(parameters, "minLetters", 2);
            var maxLetters = GetInt(parameters, "maxLetters", 8);

            if (minLetters < 2)
            {
                return (false, $"规则 {ruleId} 的 minLetters 不能小于 2。");
            }

            if (maxLetters < minLetters)
            {
                return (false, $"规则 {ruleId} 的 maxLetters 不能小于 minLetters。");
            }
        }

        return transformName switch
        {
            "trim_whitespace" => (true, null),
            "english_acronym_join" => (true, null),
            _ => (false, $"规则 {ruleId} 的内建变换不受支持：{transformName}")
        };
    }

    public static string? GetString(JsonObject parameters, string key) =>
        parameters.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() : null;

    public static bool GetBool(JsonObject parameters, string key, bool defaultValue = false) =>
        parameters.TryGetPropertyValue(key, out var value) ? value?.GetValue<bool>() ?? defaultValue : defaultValue;

    public static int GetInt(JsonObject parameters, string key, int defaultValue = 0) =>
        parameters.TryGetPropertyValue(key, out var value) ? value?.GetValue<int>() ?? defaultValue : defaultValue;
}
