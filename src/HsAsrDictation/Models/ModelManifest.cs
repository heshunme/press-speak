namespace HsAsrDictation.Models;

public static class ModelManifest
{
    private static readonly AsrModelDefinition OfflineDefinition = new()
    {
        ArchiveUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2",
        ExtractedDirectoryName = "sherpa-onnx-funasr-nano-int8-2025-12-30",
        RequiredRelativePaths =
        [
            "embedding.int8.onnx",
            "encoder_adaptor.int8.onnx",
            "llm.int8.onnx",
            "Qwen3-0.6B"
        ]
    };

    private static readonly AsrModelDefinition StreamingDefinition = new()
    {
        ArchiveUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-paraformer-bilingual-zh-en.tar.bz2",
        ExtractedDirectoryName = "sherpa-onnx-streaming-paraformer-bilingual-zh-en",
        RequiredRelativePaths =
        [
            "encoder.int8.onnx",
            "decoder.int8.onnx",
            "tokens.txt"
        ]
    };

    public static AsrModelDefinition GetDefinition(AsrModelKind kind) =>
        kind switch
        {
            AsrModelKind.Offline => OfflineDefinition,
            AsrModelKind.Streaming => StreamingDefinition,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知模型类型。")
        };
}
