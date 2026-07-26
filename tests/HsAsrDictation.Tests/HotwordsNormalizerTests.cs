using HsAsrDictation.Asr;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotwordsNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n,;，；\t")]
    public void Normalize_ReturnsEmpty_WhenInputHasNoHotwords(string? rawText)
    {
        Assert.Empty(HotwordsNormalizer.Normalize(rawText));
        Assert.Equal(string.Empty, HotwordsNormalizer.NormalizeToStorage(rawText));
    }

    [Fact]
    public void Normalize_SplitsOnAsciiAndChineseSeparators()
    {
        var hotwords = HotwordsNormalizer.Normalize("张三,李四；王五\n赵六，钱七;孙八\t周九");

        Assert.Equal(["张三", "李四", "王五", "赵六", "钱七", "孙八", "周九"], hotwords);
    }

    [Fact]
    public void Normalize_TrimsWhitespaceAndDropsEmptyEntries()
    {
        var hotwords = HotwordsNormalizer.Normalize("  sherpa-onnx  ,\n  , 声学模型 ");

        Assert.Equal(["sherpa-onnx", "声学模型"], hotwords);
    }

    [Fact]
    public void Normalize_DeduplicatesPreservingFirstOccurrenceOrder()
    {
        var hotwords = HotwordsNormalizer.Normalize("张三\n李四\n张三\n王五\n李四");

        Assert.Equal(["张三", "李四", "王五"], hotwords);
    }

    [Fact]
    public void Normalize_DropsEntriesLongerThanMaxHotwordLength()
    {
        var tooLong = new string('长', HotwordsNormalizer.MaxHotwordLength + 1);
        var atLimit = new string('词', HotwordsNormalizer.MaxHotwordLength);

        var hotwords = HotwordsNormalizer.Normalize($"{tooLong}\n{atLimit}\n正常");

        Assert.Equal([atLimit, "正常"], hotwords);
    }

    [Fact]
    public void Normalize_CapsAtMaxHotwordCount()
    {
        var rawText = string.Join('\n', Enumerable.Range(1, HotwordsNormalizer.MaxHotwordCount + 20).Select(i => $"词{i}"));

        var hotwords = HotwordsNormalizer.Normalize(rawText);

        Assert.Equal(HotwordsNormalizer.MaxHotwordCount, hotwords.Count);
        Assert.Equal("词1", hotwords[0]);
        Assert.Equal($"词{HotwordsNormalizer.MaxHotwordCount}", hotwords[^1]);
    }

    [Fact]
    public void NormalizeToStorage_JoinsWithNewline_AndIsIdempotent()
    {
        var storage = HotwordsNormalizer.NormalizeToStorage("张三, 李四；  王五");

        Assert.Equal("张三\n李四\n王五", storage);
        Assert.Equal(storage, HotwordsNormalizer.NormalizeToStorage(storage));
    }
}
