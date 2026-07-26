using System.Text.RegularExpressions;

namespace HsAsrDictation.Services;

internal static class DictationTextNormalizer
{
    public static string Normalize(string input)
    {
        var collapsed = Regex.Replace(input.Trim(), @"\s+", " ");
        return collapsed.Replace(" ,", ",").Replace(" .", ".");
    }
}
