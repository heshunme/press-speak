using System.Text.Json.Nodes;

namespace HsAsrDictation.PostProcessing.Models;

public sealed class RuleDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public int Order { get; set; } = 1000;

    public JsonObject Parameters { get; set; } = [];
}
