using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

public sealed class DictationStatus
{
    public DictationState State { get; init; }

    public RecognitionMode Mode { get; init; }

    public string OverlayText { get; init; } = string.Empty;

    public string PreviewText { get; init; } = string.Empty;

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewText);
}
