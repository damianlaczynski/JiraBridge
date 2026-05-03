using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Storage;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class ConflictDiffFormatterTests
{
  [Fact]
  public void Build_WhenFieldsRelationsAndDescriptionDiffer_ReturnsDetailedDiff()
  {
    ArtifactDocument document = CreateDocument(
      Path.Combine("C:", "repo", "backlog", "story.md"),
      "Repository title",
      description: "line one\nline repo");

    var payload = new JiraIssuePayload(
      "SCRUM",
      "Story",
      "Repository title",
      "line one\nline repo",
      "SCRUM-2",
      "SCRUM-1",
      Path.Combine("backlog", "epic.md"),
      new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
      {
        ["Blocks"] = ["SCRUM-3"],
        ["Relates"] = ["SCRUM-4"]
      });

    var remoteIssue = new JiraRemoteIssue(
      "SCRUM-2",
      "Bug",
      "Done",
      "Jira title",
      "line one\nline jira",
      DateTimeOffset.UtcNow,
      null,
      [
        new JiraRemoteLink("Blocks", null, "SCRUM-9")
      ]);

    string diff = ConflictDiffFormatter.Build(document, payload, remoteIssue);

    Assert.Contains("Summary:", diff);
    Assert.Contains("repo : Repository title", diff);
    Assert.Contains("jira : Jira title", diff);
    Assert.Contains("Issue Type:", diff);
    Assert.Contains("Parent:", diff);
    Assert.Contains("Relations:", diff);
    Assert.Contains("Blocks:", diff);
    Assert.Contains("Relates:", diff);
    Assert.Contains("Repository Description vs Jira Description:", diff);
    Assert.Contains("- line repo", diff);
    Assert.Contains("+ line jira", diff);
  }

  [Fact]
  public void Build_WhenPayloadMatchesRemote_ReturnsEmpty()
  {
    ArtifactDocument document = CreateDocument(
      Path.Combine("C:", "repo", "backlog", "story.md"),
      "Story",
      description: "same");

    var payload = new JiraIssuePayload(
      "SCRUM",
      "Story",
      "Story",
      "same",
      "SCRUM-2",
      null,
      null,
      new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
      {
        ["Blocks"] = ["SCRUM-3"]
      });

    var remoteIssue = new JiraRemoteIssue(
      "SCRUM-2",
      "Story",
      "To Do",
      "Story",
      "same",
      DateTimeOffset.UtcNow,
      null,
      [
        new JiraRemoteLink("Blocks", null, "SCRUM-3")
      ]);

    string diff = ConflictDiffFormatter.Build(document, payload, remoteIssue);

    Assert.Equal(string.Empty, diff);
  }

  private static ArtifactDocument CreateDocument(string path, string title, string description)
  {
    var descriptionSection = new SectionContent();
    foreach (string line in description.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
    {
      descriptionSection.BodyLines.Add(line);
    }

    return new ArtifactDocument
    {
      Path = path,
      Title = title,
      Sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase)
      {
        ["Description"] = descriptionSection
      }
    };
  }
}
