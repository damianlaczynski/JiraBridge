namespace JiraBridge.Domain.Artifacts;

public sealed record ArtifactLocation(string RelativePath, string? JiraIssueKey);
