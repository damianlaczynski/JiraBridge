namespace JiraBridge.Infrastructure.Storage;

public static class ArtifactFileUpdater
{
  public static void WriteSyncMetadata(
    string filePath,
    string? issueKey,
    string localHash,
    string remoteHash)
  {
    var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Jira Last Synced Local Hash"] = localHash,
      ["Jira Last Synced Remote Hash"] = remoteHash
    };

    if (!string.IsNullOrWhiteSpace(issueKey))
    {
      updates["Jira Issue Key"] = issueKey;
    }

    UpdateMetadata(filePath, updates);
  }

  public static void WriteDescriptionBody(string filePath, string body)
  {
    List<string> lines = File.ReadAllLines(filePath).ToList();
    int descriptionHeaderIndex = lines.FindIndex(line => string.Equals(line.Trim(), "## Description", StringComparison.OrdinalIgnoreCase));
    if (descriptionHeaderIndex < 0)
    {
      throw new InvalidOperationException($"Could not find '## Description' section in file: {filePath}");
    }

    int sectionStart = descriptionHeaderIndex + 1;
    int nextSectionIndex = lines.FindIndex(sectionStart, line => line.TrimStart().StartsWith("## ", StringComparison.Ordinal));
    if (nextSectionIndex < 0)
    {
      nextSectionIndex = lines.Count;
    }

    while (sectionStart < nextSectionIndex)
    {
      lines.RemoveAt(sectionStart);
      nextSectionIndex--;
    }

    List<string> bodyLines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
    lines.Insert(sectionStart, string.Empty);
    lines.InsertRange(sectionStart + 1, bodyLines);
    if (bodyLines.Count == 0 || !string.IsNullOrWhiteSpace(bodyLines[^1]))
    {
      lines.Insert(sectionStart + 1 + bodyLines.Count, string.Empty);
    }

    File.WriteAllLines(filePath, lines);
  }

  private static void UpdateMetadata(string filePath, IReadOnlyDictionary<string, string> updates)
  {
    List<string> lines = File.ReadAllLines(filePath).ToList();
    int metadataHeaderIndex = lines.FindIndex(line => string.Equals(line.Trim(), "## Metadata", StringComparison.OrdinalIgnoreCase));
    if (metadataHeaderIndex < 0)
    {
      throw new InvalidOperationException($"Could not find '## Metadata' section in file: {filePath}");
    }

    int nextSectionIndex = lines.FindIndex(metadataHeaderIndex + 1, line => line.TrimStart().StartsWith("## ", StringComparison.Ordinal));
    if (nextSectionIndex < 0)
    {
      nextSectionIndex = lines.Count;
    }

    var pending = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
    HashSet<string> obsoleteKeys =
    [
      "Jira Last Synced At",
      "Jira Last Synced Updated At",
      "Jira Last Synced Repo Hash"
    ];

    for (int index = metadataHeaderIndex + 1; index < nextSectionIndex; index++)
    {
      string trimmed = lines[index].Trim();
      if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
      {
        continue;
      }

      string item = trimmed[2..];
      int separatorIndex = item.IndexOf(':');
      if (separatorIndex <= 0)
      {
        continue;
      }

      string key = item[..separatorIndex].Trim();
      if (obsoleteKeys.Contains(key))
      {
        lines.RemoveAt(index);
        index--;
        nextSectionIndex--;
        continue;
      }

      if (!pending.TryGetValue(key, out string? value))
      {
        continue;
      }

      string indentation = lines[index][..lines[index].IndexOf('-')];
      lines[index] = $"{indentation}- {key}: {value}";
      pending.Remove(key);
    }

    if (pending.Count > 0)
    {
      int insertIndex = nextSectionIndex;
      foreach ((string key, string value) in pending)
      {
        lines.Insert(insertIndex, $"- {key}: {value}");
        insertIndex++;
      }
    }

    File.WriteAllLines(filePath, lines);
  }
}
