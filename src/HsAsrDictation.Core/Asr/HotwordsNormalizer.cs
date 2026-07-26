using System.Text;

namespace HsAsrDictation.Asr;

/// <summary>
/// 热词文本的清洗与规范化。
/// FunASR-Nano 的热词经 LLM prompt 注入（sherpa-onnx 端按逗号/分号/换行等分隔符切分），
/// 这里统一把用户输入清洗为"每行一个词"的规范存储形式，可直接传给
/// <c>OfflineFunAsrNanoModelConfig.Hotwords</c>（换行是其原生支持的分隔符之一）。
/// </summary>
public static class HotwordsNormalizer
{
    /// <summary>热词是 prompt 软偏置，词表过大会稀释偏置效果并拖慢首 token，因此设数量上限。</summary>
    public const int MaxHotwordCount = 100;

    /// <summary>单个热词长度上限（按 char 计），超长条目视为误输入直接丢弃。</summary>
    public const int MaxHotwordLength = 32;

    private static readonly char[] Separators =
    [
        ',', ';', '\n', '\r', '\t',
        '，', // ，
        '；'  // ；
    ];

    /// <summary>清洗为有序去重的热词列表：切分、去首尾空白、去空、去超长、去重、截断到数量上限。</summary>
    public static IReadOnlyList<string> Normalize(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var segment in rawText.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var hotword = segment.Trim();
            if (hotword.Length == 0 || hotword.Length > MaxHotwordLength)
            {
                continue;
            }

            if (seen.Add(hotword))
            {
                result.Add(hotword);
                if (result.Count >= MaxHotwordCount)
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>规范化为存储/引擎两用的字符串形式（每行一个词，无热词时为空串）。</summary>
    public static string NormalizeToStorage(string? rawText)
    {
        var hotwords = Normalize(rawText);
        if (hotwords.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var hotword in hotwords)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(hotword);
        }

        return builder.ToString();
    }
}
