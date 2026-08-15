using HsAsrDictation.Models;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Asr;

/// <summary>
/// 引擎初始化指纹：描述"当前识别器是按哪份设置构建的"（模型根路径；离线引擎含规范化热词）。
/// 重复初始化（每次听写、每次开始流式录音）时指纹未变即可跳过重复 provisioning（目录校验+日志）；
/// 设置保存重建、重新下载等显式路径以 forceReprovision 强制走完整 provisioning。
/// 不可变引用类型：字段引用读写原子，Unload/替换并发时不会读到撕裂的组合。
/// </summary>
public sealed class EngineInitFingerprint
{
    private readonly string _modelRootPath;
    private readonly string _hotwords;

    private EngineInitFingerprint(string modelRootPath, string hotwords)
    {
        _modelRootPath = modelRootPath;
        _hotwords = hotwords;
    }

    public static EngineInitFingerprint Capture(AppSettings settings, AsrModelKind kind) => kind switch
    {
        AsrModelKind.Offline => new EngineInitFingerprint(
            settings.OfflineModelRootPath,
            HotwordsNormalizer.NormalizeToStorage(settings.Hotwords)),
        AsrModelKind.Streaming => new EngineInitFingerprint(
            settings.StreamingModelRootPath,
            string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知模型类型。")
    };

    public bool Matches(EngineInitFingerprint other) =>
        string.Equals(_modelRootPath, other._modelRootPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_hotwords, other._hotwords, StringComparison.Ordinal);
}
