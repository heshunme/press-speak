namespace HsAsrDictation.Models;

public sealed class AsrModelDefinition
{
    public required string ArchiveUrl { get; init; }

    public required string ExtractedDirectoryName { get; init; }

    public required string[] RequiredRelativePaths { get; init; }
}
