namespace HsAsrDictation.Models;

public static class ModelManifest
{
    public const string ArchiveUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2";

    public const string ExtractedDirectoryName = "sherpa-onnx-funasr-nano-int8-2025-12-30";

    public static readonly string[] RequiredRelativePaths =
    [
        "embedding.int8.onnx",
        "encoder_adaptor.int8.onnx",
        "llm.int8.onnx",
        "Qwen3-0.6B"
    ];
}
