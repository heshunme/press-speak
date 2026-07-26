namespace HsAsrDictation.PostProcessing.Models;

public sealed class PostProcessingConfig
{
    public int Version { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public List<RuleDefinition> Rules { get; set; } = [];
}
