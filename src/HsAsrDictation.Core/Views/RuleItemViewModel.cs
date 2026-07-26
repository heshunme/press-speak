using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Validation;

namespace HsAsrDictation.Views;

public sealed class RuleItemViewModel : INotifyPropertyChanged
{
    private string _description = string.Empty;
    private string _findText = string.Empty;
    private bool _ignoreCase;
    private string _kind = "exact_replace";
    private int _maxLetters = 8;
    private int _minLetters = 2;
    private string _name = string.Empty;
    private int _order;
    private string _pattern = string.Empty;
    private string _regexOptions = "None";
    private string _replacement = string.Empty;
    private string _replaceText = string.Empty;
    private string _transformName = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                if (string.Equals(value, "exact_replace", StringComparison.Ordinal))
                {
                    TransformName = string.Empty;
                }
                else if (string.Equals(value, "regex_replace", StringComparison.Ordinal))
                {
                    TransformName = string.Empty;
                }

                OnPropertyChanged(nameof(KindDisplay));
                OnPropertyChanged(nameof(IsExactReplace));
                OnPropertyChanged(nameof(IsRegexReplace));
                OnPropertyChanged(nameof(IsBuiltInTransform));
                OnPropertyChanged(nameof(IsKindEditable));
            }
        }
    }

    public string KindDisplay => Kind switch
    {
        "exact_replace" => "精确替换",
        "regex_replace" => "正则替换",
        "built_in_transform" => "内建变换",
        _ => Kind
    };

    public bool IsExactReplace => string.Equals(Kind, "exact_replace", StringComparison.Ordinal);

    public bool IsRegexReplace => string.Equals(Kind, "regex_replace", StringComparison.Ordinal);

    public bool IsBuiltInTransform => string.Equals(Kind, "built_in_transform", StringComparison.Ordinal);

    public bool IsKindEditable => !IsBuiltIn;

    public string FindText
    {
        get => _findText;
        set => SetProperty(ref _findText, value);
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value);
    }

    public bool IgnoreCase
    {
        get => _ignoreCase;
        set => SetProperty(ref _ignoreCase, value);
    }

    public string Pattern
    {
        get => _pattern;
        set => SetProperty(ref _pattern, value);
    }

    public string Replacement
    {
        get => _replacement;
        set => SetProperty(ref _replacement, value);
    }

    public string RegexOptions
    {
        get => _regexOptions;
        set => SetProperty(ref _regexOptions, value);
    }

    public string TransformName
    {
        get => _transformName;
        set => SetProperty(ref _transformName, value);
    }

    public int MinLetters
    {
        get => _minLetters;
        set => SetProperty(ref _minLetters, value);
    }

    public int MaxLetters
    {
        get => _maxLetters;
        set => SetProperty(ref _maxLetters, value);
    }

    public static RuleItemViewModel FromDefinition(RuleDefinition definition)
    {
        var viewModel = new RuleItemViewModel
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Kind = definition.Kind,
            IsEnabled = definition.IsEnabled,
            IsBuiltIn = definition.IsBuiltIn,
            Order = definition.Order
        };

        if (definition.Kind == "exact_replace")
        {
            viewModel.FindText = RuleValidator.GetString(definition.Parameters, "find") ?? string.Empty;
            viewModel.ReplaceText = RuleValidator.GetString(definition.Parameters, "replace") ?? string.Empty;
            viewModel.IgnoreCase = RuleValidator.GetBool(definition.Parameters, "ignoreCase");
        }
        else if (definition.Kind == "regex_replace")
        {
            viewModel.Pattern = RuleValidator.GetString(definition.Parameters, "pattern") ?? string.Empty;
            viewModel.Replacement = RuleValidator.GetString(definition.Parameters, "replacement") ?? string.Empty;
            viewModel.RegexOptions = RuleValidator.GetString(definition.Parameters, "options") ?? "None";
        }
        else if (definition.Kind == "built_in_transform")
        {
            viewModel.TransformName = RuleValidator.GetString(definition.Parameters, "transformName") ?? string.Empty;
            viewModel.MinLetters = RuleValidator.GetInt(definition.Parameters, "minLetters", 2);
            viewModel.MaxLetters = RuleValidator.GetInt(definition.Parameters, "maxLetters", 8);
        }

        return viewModel;
    }

    public RuleItemViewModel CreateCopy()
    {
        return new RuleItemViewModel
        {
            Id = IsBuiltIn ? Id : $"user.{Guid.NewGuid():N}",
            Name = Name,
            Description = Description,
            Kind = Kind,
            IsEnabled = IsEnabled,
            IsBuiltIn = false,
            Order = Order,
            FindText = FindText,
            ReplaceText = ReplaceText,
            IgnoreCase = IgnoreCase,
            Pattern = Pattern,
            Replacement = Replacement,
            RegexOptions = RegexOptions,
            TransformName = TransformName,
            MinLetters = MinLetters,
            MaxLetters = MaxLetters
        };
    }

    public RuleDefinition ToDefinition()
    {
        var parameters = new JsonObject();

        if (IsExactReplace)
        {
            parameters["find"] = FindText;
            parameters["replace"] = ReplaceText;
            parameters["ignoreCase"] = IgnoreCase;
        }
        else if (IsRegexReplace)
        {
            parameters["pattern"] = Pattern;
            parameters["replacement"] = Replacement;
            parameters["options"] = string.IsNullOrWhiteSpace(RegexOptions) ? "None" : RegexOptions;
        }
        else if (IsBuiltInTransform)
        {
            parameters["transformName"] = TransformName;
            if (string.Equals(TransformName, "english_acronym_join", StringComparison.Ordinal))
            {
                parameters["minLetters"] = MinLetters;
                parameters["maxLetters"] = MaxLetters;
                parameters["preserveCase"] = true;
                parameters["onlyAsciiLetters"] = true;
            }
        }

        return new RuleDefinition
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Kind = Kind,
            IsEnabled = IsEnabled,
            IsBuiltIn = IsBuiltIn,
            Order = Order,
            Parameters = parameters
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
