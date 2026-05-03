namespace JiraBridge.Infrastructure.Parsing;

public static class ToolSectionNames
{
  private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
  {
    "Metadata",
    "Links",
    "Relations",
    "Source",
    "Description",
    "Scope",
    "Business Goal",
    "Requirements Summary",
    "Acceptance Criteria Summary",
    "Technical Area",
    "Implementation Notes",
    "Change Type",
    "Change Summary",
    "Requested Actions"
  };

  public static bool IsToolSection(string name) => KnownSections.Contains(name);
}
