using System.Text.RegularExpressions;

namespace HsAsrDictation.PostProcessing.Validation;

public static class RegexSafetyValidator
{
    public static (bool Ok, string? Error) Validate(string pattern, string? optionsText = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return (false, "Pattern 不能为空。");
        }

        if (!TryParseOptions(optionsText, out var options, out var optionsError))
        {
            return (false, optionsError);
        }

        try
        {
            _ = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool TryParseOptions(string? optionsText, out RegexOptions options, out string? error)
    {
        options = RegexOptions.None;
        error = null;

        if (string.IsNullOrWhiteSpace(optionsText))
        {
            return true;
        }

        if (Enum.TryParse(optionsText, ignoreCase: true, out options))
        {
            return true;
        }

        error = $"无法识别正则选项：{optionsText}";
        return false;
    }
}
