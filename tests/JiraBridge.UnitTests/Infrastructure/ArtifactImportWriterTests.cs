using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class ArtifactImportWriterTests
{
  [Fact]
  public void BuildPlannedArtifactPath_BuildsChildPathUnderParentArtifact()
  {
    var issue = new JiraRemoteIssue(
      "SCRUM-15",
      "Story",
      "To Do",
      "Create API",
      "Description",
      DateTimeOffset.UtcNow,
      "SCRUM-1",
      []);

    string result = ArtifactImportWriter.BuildPlannedArtifactPath(
      Path.Combine("repo", "project-docs", "backlog"),
      issue,
      Path.Combine("repo", "project-docs", "backlog", "epic", "scrum-1-parent.md"));

    Assert.EndsWith(Path.Combine("epic", "scrum-1-parent", "scrum-15-create-api.md"), result, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void WriteImportedArtifact_WritesClickableMarkdownReferences()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);

    try
    {
      string artifactPath = Path.Combine(repoRoot, "project-docs", "backlog", "story.md");
      ArtifactImportWriter.WriteImportedArtifact(
        artifactPath,
        new JiraRemoteIssue("SCRUM-10", "Story", "To Do", "Imported story", "Body", DateTimeOffset.UtcNow, null, []),
        "../epic.md",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
          ["Blocks"] = ["../dependency.md"]
        });

      string content = File.ReadAllText(artifactPath);
      Assert.Contains("- Parent: [../epic.md](../epic.md)", content);
      Assert.Contains("- [../dependency.md](../dependency.md)", content);
      Assert.DoesNotContain("## Source", content);
      Assert.True(content.IndexOf("## Description", StringComparison.Ordinal) < content.IndexOf("## Metadata", StringComparison.Ordinal));
    }
    finally
    {
      Directory.Delete(repoRoot, recursive: true);
    }
  }
}
