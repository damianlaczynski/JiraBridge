namespace JiraBridge.Domain.Artifacts;

public sealed class ArtifactDocument
{
  public required string Path { get; init; }

  public required string Title { get; init; }

  public required Dictionary<string, SectionContent> Sections { get; init; }

  public string? JiraIssueType => GetKeyValue("Metadata", "Issue Type") ?? GetKeyValue("Metadata", "Type");

  public string? Type => JiraIssueType;

  public string? JiraIssueKey => GetKeyValue("Metadata", "Jira Issue Key");

  public string? JiraLastSyncedLocalHash =>
    GetKeyValue("Metadata", "Jira Last Synced Local Hash") ??
    GetKeyValue("Metadata", "Jira Last Synced Repo Hash");

  public string? JiraLastSyncedRemoteHash => GetKeyValue("Metadata", "Jira Last Synced Remote Hash");

  public string? Parent => GetKeyValue("Links", "Parent");

  public string RelativePath(string repoRoot) => System.IO.Path.GetRelativePath(repoRoot, Path);

  public string GetSectionBody(string sectionName)
  {
    if (!Sections.TryGetValue(sectionName, out SectionContent? section))
    {
      return string.Empty;
    }

    return string.Join(System.Environment.NewLine, section.BodyLines).Trim();
  }

  public IReadOnlyList<string> GetNestedList(string sectionName, string nestedSectionName)
  {
    if (!Sections.TryGetValue(sectionName, out SectionContent? section))
    {
      return Array.Empty<string>();
    }

    return section.NestedLists.TryGetValue(nestedSectionName, out List<string>? list)
      ? list
      : Array.Empty<string>();
  }

  public string? GetKeyValue(string sectionName, string key)
  {
    if (!Sections.TryGetValue(sectionName, out SectionContent? section))
    {
      return null;
    }

    return section.KeyValues.TryGetValue(key, out string? value) ? value : null;
  }

  public void SetKeyValue(string sectionName, string key, string value)
  {
    if (!Sections.TryGetValue(sectionName, out SectionContent? section))
    {
      return;
    }

    section.KeyValues[key] = value;
  }
}
