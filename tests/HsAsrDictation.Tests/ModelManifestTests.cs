using HsAsrDictation.Models;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class ModelManifestTests
{
    [Fact]
    public void OfflineDefinition_PointsToFunAsrNanoInt8Archive()
    {
        var definition = ModelManifest.GetDefinition(AsrModelKind.Offline);

        Assert.Contains("funasr-nano-int8", definition.ArchiveUrl);
        Assert.EndsWith(".tar.bz2", definition.ArchiveUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineDefinition_ContainsExpectedFiles()
    {
        var definition = ModelManifest.GetDefinition(AsrModelKind.Offline);

        Assert.Contains("embedding.int8.onnx", definition.RequiredRelativePaths);
        Assert.Contains("encoder_adaptor.int8.onnx", definition.RequiredRelativePaths);
        Assert.Contains("llm.int8.onnx", definition.RequiredRelativePaths);
        Assert.Contains("Qwen3-0.6B", definition.RequiredRelativePaths);
    }

    [Fact]
    public void StreamingDefinition_ContainsExpectedFiles()
    {
        var definition = ModelManifest.GetDefinition(AsrModelKind.Streaming);

        Assert.Contains("streaming-paraformer-bilingual-zh-en", definition.ArchiveUrl);
        Assert.Contains("encoder.int8.onnx", definition.RequiredRelativePaths);
        Assert.Contains("decoder.int8.onnx", definition.RequiredRelativePaths);
        Assert.Contains("tokens.txt", definition.RequiredRelativePaths);
    }
}
