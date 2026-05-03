namespace JiraBridge.Domain.Artifacts;

public sealed class SectionContent
{
  public Dictionary<string, string> KeyValues { get; } = new(StringComparer.OrdinalIgnoreCase);

  public Dictionary<string, List<string>> NestedLists { get; } = new(StringComparer.OrdinalIgnoreCase);

  public List<string> BodyLines { get; } = [];
}
