using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Services;

namespace HsAsrDictation.Audio;

/// <summary>
/// 用随发布包内置的模型创建 <see cref="SileroVadSegmenter"/>。
/// 模型缺失或原生构造失败都只记日志并返回 null——分段解码是性能优化层，不能拖垮听写。
/// </summary>
public sealed class SileroVadSegmenterFactory : IAudioSegmenterFactory
{
    private readonly SegmentedDecodeOptions _options;
    private readonly LocalLogService _logger;
    private bool _missingModelLogged;

    public SileroVadSegmenterFactory(SegmentedDecodeOptions options, LocalLogService logger)
    {
        _options = options;
        _logger = logger;
    }

    public IAudioSegmenter? TryCreate(TimeSpan bufferCapacity)
    {
        var modelPath = VadModelLocator.TryResolveModelPath();
        if (modelPath is null)
        {
            // 每次录音都会走到这里，只在第一次记日志，避免刷满日志文件。
            if (!_missingModelLogged)
            {
                _missingModelLogged = true;
                _logger.Warn(
                    $"未找到内置 VAD 模型 {VadModelLocator.ModelFileName}，本次运行将使用整段解码。");
            }

            return null;
        }

        try
        {
            return new SileroVadSegmenter(
                modelPath,
                _options.MinSilenceDuration,
                _options.MinSpeechDuration,
                _options.MaxOpenSegment,
                bufferCapacity);
        }
        catch (Exception ex)
        {
            _logger.Error("创建 VAD 分段器失败，本次听写将使用整段解码。", ex);
            return null;
        }
    }
}
