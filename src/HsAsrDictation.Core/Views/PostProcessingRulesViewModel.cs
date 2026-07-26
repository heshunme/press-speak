using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HsAsrDictation.PostProcessing.Models;

namespace HsAsrDictation.Views;

public sealed class PostProcessingRulesViewModel : INotifyPropertyChanged
{
    private bool _isRuleSystemEnabled;
    private RuleItemViewModel? _selectedRule;
    private string _testInput = string.Empty;
    private string _testOutput = string.Empty;
    private string _testTrace = string.Empty;

    public PostProcessingRulesViewModel(PostProcessingConfig config)
    {
        Load(config);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RuleItemViewModel> Rules { get; } = [];

    public bool IsRuleSystemEnabled
    {
        get => _isRuleSystemEnabled;
        set => SetProperty(ref _isRuleSystemEnabled, value);
    }

    public RuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                OnPropertyChanged(nameof(CanResetSelectedRule));
                OnPropertyChanged(nameof(CanDeleteSelectedRule));
                OnPropertyChanged(nameof(CanMoveSelectedRuleUp));
                OnPropertyChanged(nameof(CanMoveSelectedRuleDown));
            }
        }
    }

    public string TestInput
    {
        get => _testInput;
        set => SetProperty(ref _testInput, value);
    }

    public string TestOutput
    {
        get => _testOutput;
        set => SetProperty(ref _testOutput, value);
    }

    public string TestTrace
    {
        get => _testTrace;
        set => SetProperty(ref _testTrace, value);
    }

    public bool CanResetSelectedRule => SelectedRule?.IsBuiltIn == true;

    public bool CanDeleteSelectedRule => SelectedRule is not null;

    public bool CanMoveSelectedRuleUp => SelectedRule is not null && Rules.IndexOf(SelectedRule) > 0;

    public bool CanMoveSelectedRuleDown => SelectedRule is not null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;

    public void Load(PostProcessingConfig config)
    {
        Rules.Clear();
        foreach (var rule in config.Rules.OrderBy(rule => rule.Order).ThenBy(rule => rule.Id, StringComparer.Ordinal))
        {
            Rules.Add(RuleItemViewModel.FromDefinition(rule));
        }

        IsRuleSystemEnabled = config.IsEnabled;
        SelectedRule = Rules.FirstOrDefault();
    }

    public RuleItemViewModel AddRule()
    {
        var nextOrder = (Rules.Count + 1) * 100;
        var rule = new RuleItemViewModel
        {
            Id = $"user.{Guid.NewGuid():N}",
            Name = "新规则",
            Description = "请填写规则说明",
            Kind = "exact_replace",
            IsEnabled = true,
            IsBuiltIn = false,
            Order = nextOrder,
            FindText = string.Empty,
            ReplaceText = string.Empty
        };

        Rules.Add(rule);
        NormalizeOrders();
        SelectedRule = rule;
        return rule;
    }

    public RuleItemViewModel? DuplicateSelectedRule()
    {
        if (SelectedRule is null)
        {
            return null;
        }

        var copy = SelectedRule.CreateCopy();
        copy.Id = $"user.{Guid.NewGuid():N}";
        copy.IsBuiltIn = false;
        copy.Name = $"{SelectedRule.Name} - 副本";
        Rules.Insert(Math.Min(Rules.Count, Rules.IndexOf(SelectedRule) + 1), copy);
        NormalizeOrders();
        SelectedRule = copy;
        return copy;
    }

    public void DeleteSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var current = SelectedRule;
        var index = Rules.IndexOf(current);

        if (current.IsBuiltIn)
        {
            current.IsEnabled = false;
        }
        else
        {
            Rules.Remove(current);
        }

        NormalizeOrders();
        SelectedRule = Rules.Count == 0
            ? null
            : Rules[Math.Clamp(index, 0, Rules.Count - 1)];
    }

    public void MoveSelectedRule(int offset)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var currentIndex = Rules.IndexOf(SelectedRule);
        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= Rules.Count)
        {
            return;
        }

        Rules.Move(currentIndex, targetIndex);
        NormalizeOrders();
        SelectedRule = Rules[targetIndex];
    }

    public void NormalizeOrders()
    {
        for (var index = 0; index < Rules.Count; index++)
        {
            Rules[index].Order = (index + 1) * 100;
        }

        OnPropertyChanged(nameof(CanMoveSelectedRuleUp));
        OnPropertyChanged(nameof(CanMoveSelectedRuleDown));
    }

    public PostProcessingConfig BuildConfig()
    {
        NormalizeOrders();
        return new PostProcessingConfig
        {
            Version = 1,
            IsEnabled = IsRuleSystemEnabled,
            Rules = Rules.Select(rule => rule.ToDefinition()).ToList()
        };
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
