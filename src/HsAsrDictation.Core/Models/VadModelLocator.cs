using System.IO;

namespace HsAsrDictation.Models;

/// <summary>
/// 定位随发布包内置的 Silero VAD 模型。
/// 与 ASR/标点模型不同，VAD 模型不走下载与解压：它只有 643 KB，直接随产物分发，
/// 因此这里只做"找到并校验存在"，找不到时由调用方降级为全量解码（见 docs/vad-segmented-decoding-design.md）。
/// </summary>
public static class VadModelLocator
{
    public const string ModelFileName = "silero_vad.onnx";

    private static readonly string RelativeDirectory = Path.Combine("Resources", "Vad");

    /// <summary>
    /// 返回内置模型的绝对路径；文件不存在时返回 null。
    /// </summary>
    /// <param name="baseDirectory">应用目录，默认取当前程序集所在目录。</param>
    public static string? TryResolveModelPath(string? baseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        var candidate = Path.Combine(root, RelativeDirectory, ModelFileName);
        return File.Exists(candidate) ? candidate : null;
    }
}
