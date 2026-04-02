using HsAsrDictation.Models;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class ModelManifestTests
{
    [Fact]
    public void ArchiveUrl_PointsToFunAsrNanoInt8Archive()
    {
        Assert.Contains("funasr-nano-int8", ModelManifest.ArchiveUrl);
        Assert.EndsWith(".tar.bz2", ModelManifest.ArchiveUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredEntries_ContainExpectedFiles()
    {
        Assert.Contains("embedding.int8.onnx", ModelManifest.RequiredRelativePaths);
        Assert.Contains("encoder_adaptor.int8.onnx", ModelManifest.RequiredRelativePaths);
        Assert.Contains("llm.int8.onnx", ModelManifest.RequiredRelativePaths);
        Assert.Contains("Qwen3-0.6B", ModelManifest.RequiredRelativePaths);
    }
}
