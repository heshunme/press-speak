using System.Text.Json.Nodes;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Rules;
using HsAsrDictation.Views;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class RuleItemViewModelTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ToDefinition_RoundTripsChineseNumberOptions(bool convertPercentages, bool normalizeDecimalSpacing)
    {
        var definition = BuiltInRule("chinese_number_normalize");
        definition.Parameters["convertPercentages"] = convertPercentages;
        definition.Parameters["normalizeDecimalSpacing"] = normalizeDecimalSpacing;

        var viewModel = RuleItemViewModel.FromDefinition(definition);
        var saved = viewModel.ToDefinition();
        var reloaded = RuleItemViewModel.FromDefinition(saved);

        Assert.True(reloaded.IsChineseNumberNormalize);
        Assert.False(reloaded.IsEnglishAcronymJoin);
        Assert.Equal(convertPercentages, reloaded.ConvertPercentages);
        Assert.Equal(normalizeDecimalSpacing, reloaded.NormalizeDecimalSpacing);
        Assert.Equal("chinese_number_normalize", saved.Parameters["transformName"]!.GetValue<string>());
        Assert.Equal(convertPercentages, saved.Parameters["convertPercentages"]!.GetValue<bool>());
        Assert.Equal(normalizeDecimalSpacing, saved.Parameters["normalizeDecimalSpacing"]!.GetValue<bool>());
        Assert.False(saved.Parameters.ContainsKey("minLetters"));
        Assert.False(saved.Parameters.ContainsKey("canonicalTerms"));
        Assert.Equal(definition.Id, saved.Id);
        Assert.Equal(definition.Name, saved.Name);
        Assert.Equal(definition.IsBuiltIn, saved.IsBuiltIn);
        Assert.Equal(definition.IsEnabled, saved.IsEnabled);
        Assert.Equal(definition.Order, saved.Order);
    }

    [Fact]
    public void FromDefinition_UsesEnabledDefaults_WhenChineseNumberOptionsAreMissing()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("chinese_number_normalize"));

        Assert.True(viewModel.ConvertPercentages);
        Assert.True(viewModel.NormalizeDecimalSpacing);
    }

    [Theory]
    [InlineData("API\nGPT\nOpenAI")]
    [InlineData("API, GPT, OpenAI")]
    [InlineData("")]
    public void ToDefinition_RoundTripsCanonicalTermsWithoutReplacingEmptyList(string canonicalTerms)
    {
        var definition = BuiltInRule("english_case_normalize");
        definition.Parameters["canonicalTerms"] = canonicalTerms;

        var viewModel = RuleItemViewModel.FromDefinition(definition);
        var saved = viewModel.ToDefinition();
        var reloaded = RuleItemViewModel.FromDefinition(saved);

        Assert.True(reloaded.IsEnglishCaseNormalize);
        Assert.False(reloaded.IsEnglishAcronymJoin);
        Assert.Equal(canonicalTerms, reloaded.CanonicalTerms);
        Assert.Equal("english_case_normalize", saved.Parameters["transformName"]!.GetValue<string>());
        Assert.Equal(canonicalTerms, saved.Parameters["canonicalTerms"]!.GetValue<string>());
        Assert.False(saved.Parameters.ContainsKey("minLetters"));
        Assert.False(saved.Parameters.ContainsKey("convertPercentages"));
    }

    [Fact]
    public void FromDefinition_UsesDefaultCanonicalTerms_WhenParameterIsMissing()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("english_case_normalize"));

        Assert.Equal(EnglishCaseNormalizeRule.DefaultCanonicalTerms, viewModel.CanonicalTerms);
        Assert.Equal(
            EnglishCaseNormalizeRule.DefaultCanonicalTerms,
            viewModel.ToDefinition().Parameters["canonicalTerms"]!.GetValue<string>());
    }

    [Fact]
    public void ToDefinition_PersistsEditedChineseNumberOptions()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("chinese_number_normalize"));
        viewModel.ConvertPercentages = false;
        viewModel.NormalizeDecimalSpacing = false;

        var reloaded = RuleItemViewModel.FromDefinition(viewModel.ToDefinition());

        Assert.False(reloaded.ConvertPercentages);
        Assert.False(reloaded.NormalizeDecimalSpacing);
    }

    [Fact]
    public void ToDefinition_PreservesClearedCanonicalTerms()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("english_case_normalize"));
        viewModel.CanonicalTerms = string.Empty;

        var reloaded = RuleItemViewModel.FromDefinition(viewModel.ToDefinition());

        Assert.Empty(reloaded.CanonicalTerms);
    }

    [Fact]
    public void CreateCopy_PreservesChineseNumberOptionsAndAllowsIndependentEdits()
    {
        var original = RuleItemViewModel.FromDefinition(BuiltInRule("chinese_number_normalize"));
        original.ConvertPercentages = false;
        original.NormalizeDecimalSpacing = false;

        var copy = original.CreateCopy();
        var reloaded = RuleItemViewModel.FromDefinition(copy.ToDefinition());

        Assert.True(copy.IsChineseNumberNormalize);
        Assert.False(copy.IsBuiltIn);
        Assert.False(reloaded.ConvertPercentages);
        Assert.False(reloaded.NormalizeDecimalSpacing);

        copy.ConvertPercentages = true;
        copy.NormalizeDecimalSpacing = true;

        Assert.False(original.ConvertPercentages);
        Assert.False(original.NormalizeDecimalSpacing);
    }

    [Theory]
    [InlineData("API\nPressSpeak")]
    [InlineData("")]
    public void CreateCopy_PreservesCanonicalTermsAndAllowsIndependentEdits(string canonicalTerms)
    {
        var original = RuleItemViewModel.FromDefinition(BuiltInRule("english_case_normalize"));
        original.CanonicalTerms = canonicalTerms;

        var copy = original.CreateCopy();
        var reloaded = RuleItemViewModel.FromDefinition(copy.ToDefinition());

        Assert.True(copy.IsEnglishCaseNormalize);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(canonicalTerms, reloaded.CanonicalTerms);

        copy.CanonicalTerms = "OtherTerm";

        Assert.Equal(canonicalTerms, original.CanonicalTerms);
    }

    [Fact]
    public void TransformName_WhenChanged_NotifiesAndSwitchesParameterPanels()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("english_acronym_join"));
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.TransformName = "chinese_number_normalize";

        Assert.False(viewModel.IsEnglishAcronymJoin);
        Assert.True(viewModel.IsChineseNumberNormalize);
        Assert.False(viewModel.IsEnglishCaseNormalize);
        Assert.False(viewModel.IsTrimWhitespace);
        Assert.Contains(nameof(RuleItemViewModel.IsEnglishAcronymJoin), notifications);
        Assert.Contains(nameof(RuleItemViewModel.IsChineseNumberNormalize), notifications);
        Assert.Contains(nameof(RuleItemViewModel.IsEnglishCaseNormalize), notifications);
        Assert.Contains(nameof(RuleItemViewModel.IsTrimWhitespace), notifications);

        viewModel.TransformName = "english_case_normalize";

        Assert.False(viewModel.IsChineseNumberNormalize);
        Assert.True(viewModel.IsEnglishCaseNormalize);
    }

    [Fact]
    public void Kind_WhenChangedToExactReplace_HidesTransformParameterPanels()
    {
        var viewModel = RuleItemViewModel.FromDefinition(BuiltInRule("english_case_normalize"));
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.Kind = "exact_replace";

        Assert.True(viewModel.IsExactReplace);
        Assert.False(viewModel.IsEnglishCaseNormalize);
        Assert.False(viewModel.IsChineseNumberNormalize);
        Assert.False(viewModel.IsEnglishAcronymJoin);
        Assert.False(viewModel.IsTrimWhitespace);
        Assert.Contains(nameof(RuleItemViewModel.IsEnglishCaseNormalize), notifications);
    }

    [Fact]
    public void ToDefinition_RoundTripsExistingAcronymOptions()
    {
        var definition = BuiltInRule("english_acronym_join");
        definition.Parameters["minLetters"] = 3;
        definition.Parameters["maxLetters"] = 6;

        var viewModel = RuleItemViewModel.FromDefinition(definition);
        var saved = viewModel.ToDefinition();

        Assert.True(viewModel.IsEnglishAcronymJoin);
        Assert.Equal(3, saved.Parameters["minLetters"]!.GetValue<int>());
        Assert.Equal(6, saved.Parameters["maxLetters"]!.GetValue<int>());
        Assert.True(saved.Parameters["preserveCase"]!.GetValue<bool>());
        Assert.True(saved.Parameters["onlyAsciiLetters"]!.GetValue<bool>());
        Assert.False(saved.Parameters.ContainsKey("canonicalTerms"));
        Assert.False(saved.Parameters.ContainsKey("convertPercentages"));
    }

    private static RuleDefinition BuiltInRule(string transformName) => new()
    {
        Id = $"builtin.{transformName}",
        Name = "测试内建规则",
        Description = "测试规则参数保存",
        Kind = "built_in_transform",
        IsBuiltIn = true,
        IsEnabled = false,
        Order = 500,
        Parameters = new JsonObject { ["transformName"] = transformName }
    };
}
