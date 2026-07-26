using System.IO;
using HsAsrDictation.Services;

namespace HsAsrDictation.Models;

public static class PunctuationModelManifest
{
    public const string ArchiveUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/" +
        "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2";

    public const string ExtractedDirectoryName = "ct-transformer-zh-en-int8";

    public const string RequiredFileName = "model.int8.onnx";

    public static string[] RequiredRelativePaths { get; } = [RequiredFileName];

    public static string GetModelRootPath(string? modelRootPath = null) =>
        string.IsNullOrWhiteSpace(modelRootPath)
            ? Path.Combine(AppPaths.DefaultModelRootPath, "punctuation")
            : modelRootPath;

    public static string GetModelDirectory(string? modelRootPath = null) =>
        Path.Combine(GetModelRootPath(modelRootPath), ExtractedDirectoryName);

    public static string ModelRootPath => GetModelRootPath();

    public static string ModelDirectory => GetModelDirectory();
}
