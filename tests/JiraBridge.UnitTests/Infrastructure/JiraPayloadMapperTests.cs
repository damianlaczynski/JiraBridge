using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Jira;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class JiraPayloadMapperTests
{
  [Fact]
  public void Map_ResolvesParentIssueRuntimeKeysAndRelationTargets()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string epicPath = Path.Combine(repoRoot, "backlog", "epic.md");
    string storyPath = Path.Combine(repoRoot, "backlog", "story.md");
    string taskPath = Path.Combine(repoRoot, "backlog", "task.md");
    string externalPath = Path.Combine(repoRoot, "backlog", "external.md");

    ArtifactDocument epic = CreateDocument(epicPath, "Epic", issueType: "Epic", issueKey: "SCRUM-1");
    ArtifactDocument story = CreateDocument(
      storyPath,
      "Story",
      issueType: "Story",
      parent: "epic.md",
      issueKey: "SCRUM-2",
      relations: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
      {
        ["Blocks"] = ["task.md", "none", "external.md"]
      },
      description: "Story body");

    ArtifactDocument task = CreateDocument(taskPath, "Task", issueType: "Task");
    ArtifactDocument external = CreateDocument(externalPath, "External", issueType: "Task", issueKey: "SCRUM-9");

    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [epicPath] = epic,
      [storyPath] = story,
      [taskPath] = task,
      [externalPath] = external
    };

    IReadOnlyDictionary<string, string> runtimeIssueKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      [taskPath] = "SCRUM-3"
    };

    var jiraConfiguration = new RepositoryJiraConfiguration("SCRUM", "100", "Scrum", "https://example", [], [], []);

    JiraIssuePayload payload = JiraPayloadMapper.Map(story, documents, runtimeIssueKeys, jiraConfiguration, repoRoot);

    Assert.Equal("SCRUM", payload.ProjectKey);
    Assert.Equal("Story", payload.IssueType);
    Assert.Equal("Story", payload.Summary);
    Assert.Equal("Story body", payload.Description);
    Assert.Equal("SCRUM-2", payload.ExistingIssueKey);
    Assert.Equal("SCRUM-1", payload.ParentIssueKey);
    Assert.Equal(Path.Combine("backlog", "epic.md"), payload.ParentArtifactPath);
    Assert.Equal(["SCRUM-3", "SCRUM-9"], payload.Relationships["Blocks"]);
  }

  [Fact]
  public void Map_WhenRelatedArtifactHasNoIssueKey_FallsBackToRepoRelativePath()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string storyPath = Path.Combine(repoRoot, "backlog", "story.md");
    string draftPath = Path.Combine(repoRoot, "backlog", "draft.md");

    ArtifactDocument story = CreateDocument(
      storyPath,
      "Story",
      issueType: "Story",
      relations: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
      {
        ["Relates"] = ["draft.md"]
      });

    ArtifactDocument draft = CreateDocument(draftPath, "Draft", issueType: "Task");

    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [storyPath] = story,
      [draftPath] = draft
    };

    var jiraConfiguration = new RepositoryJiraConfiguration("SCRUM", "100", "Scrum", "https://example", [], [], []);

    JiraIssuePayload payload = JiraPayloadMapper.Map(story, documents, new Dictionary<string, string>(), jiraConfiguration, repoRoot);

    Assert.Equal([Path.Combine("backlog", "draft.md")], payload.Relationships["Relates"]);
  }

  [Fact]
  public void Map_WhenSprintMappingEnabled_ResolvesSprintFromPath()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string backlogRoot = Path.Combine(repoRoot, "docs", "jira-bridge");
    string storyPath = Path.Combine(backlogRoot, "sprint-sprint-24", "story", "story.md");

    ArtifactDocument story = CreateDocument(storyPath, "Story", issueType: "Story");
    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [storyPath] = story
    };

    var jiraConfiguration = new RepositoryJiraConfiguration(
      "SCRUM",
      "100",
      "Scrum",
      "https://example",
      [],
      [],
      [],
      SprintFieldId: "customfield_10020",
      Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]);

    JiraIssuePayload payload = JiraPayloadMapper.Map(
      story,
      documents,
      new Dictionary<string, string>(),
      jiraConfiguration,
      repoRoot,
      backlogRoot,
      sprintMappingEnabled: true);

    Assert.True(payload.ApplySprintMapping);
    Assert.Equal(24, payload.SprintId);
  }

  [Fact]
  public void Map_WhenSprintMappingDisabled_DoesNotApplySprint()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string backlogRoot = Path.Combine(repoRoot, "docs", "jira-bridge");
    string storyPath = Path.Combine(backlogRoot, "sprint-sprint-24", "story", "story.md");

    ArtifactDocument story = CreateDocument(storyPath, "Story", issueType: "Story");
    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [storyPath] = story
    };

    var jiraConfiguration = new RepositoryJiraConfiguration(
      "SCRUM",
      "100",
      "Scrum",
      "https://example",
      [],
      [],
      [],
      SprintFieldId: "customfield_10020",
      Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]);

    JiraIssuePayload payload = JiraPayloadMapper.Map(
      story,
      documents,
      new Dictionary<string, string>(),
      jiraConfiguration,
      repoRoot,
      backlogRoot,
      sprintMappingEnabled: false);

    Assert.False(payload.ApplySprintMapping);
    Assert.Null(payload.SprintId);
  }

  [Fact]
  public void Map_WhenSprintMappingEnabled_AndJiraReportsNoSprints_OmitsSprintIdForSprintFolderArtifact()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string backlogRoot = Path.Combine(repoRoot, "docs", "jira-bridge");
    string storyPath = Path.Combine(backlogRoot, "sprint-scrum-sprint-1", "SCRUM-6", "SCRUM-6.md");

    ArtifactDocument story = CreateDocument(storyPath, "Story", issueType: "Story");
    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [storyPath] = story
    };

    var jiraConfiguration = new RepositoryJiraConfiguration(
      "SCRUM",
      "100",
      "Scrum",
      "https://example",
      [],
      [],
      [],
      SprintFieldId: "customfield_10020",
      Sprints: []);

    JiraIssuePayload payload = JiraPayloadMapper.Map(
      story,
      documents,
      new Dictionary<string, string>(),
      jiraConfiguration,
      repoRoot,
      backlogRoot,
      sprintMappingEnabled: true);

    Assert.True(payload.ApplySprintMapping);
    Assert.Null(payload.SprintId);
  }

  [Fact]
  public void Map_WhenSprintMappingEnabled_AndSprintSlugMismatch_ThrowsWithKnownSegments()
  {
    string repoRoot = Path.Combine("C:", "repo");
    string backlogRoot = Path.Combine(repoRoot, "docs", "jira-bridge");
    string storyPath = Path.Combine(backlogRoot, "sprint-unknown-sprint", "x", "x.md");

    ArtifactDocument story = CreateDocument(storyPath, "Story", issueType: "Story");
    IReadOnlyDictionary<string, ArtifactDocument> documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase)
    {
      [storyPath] = story
    };

    var jiraConfiguration = new RepositoryJiraConfiguration(
      "SCRUM",
      "100",
      "Scrum",
      "https://example",
      [],
      [],
      [],
      SprintFieldId: "customfield_10020",
      Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]);

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
      JiraPayloadMapper.Map(
        story,
        documents,
        new Dictionary<string, string>(),
        jiraConfiguration,
        repoRoot,
        backlogRoot,
        sprintMappingEnabled: true));

    Assert.Contains("sprint-unknown-sprint", ex.Message, StringComparison.Ordinal);
    Assert.Contains("sprint-sprint-24", ex.Message, StringComparison.Ordinal);
  }

  private static ArtifactDocument CreateDocument(
    string path,
    string title,
    string issueType,
    string? parent = null,
    string? issueKey = null,
    Dictionary<string, List<string>>? relations = null,
    string description = "")
  {
    var metadata = new SectionContent();
    metadata.KeyValues["Issue Type"] = issueType;
    if (!string.IsNullOrWhiteSpace(issueKey))
    {
      metadata.KeyValues["Jira Issue Key"] = issueKey;
    }

    var descriptionSection = new SectionContent();
    if (!string.IsNullOrWhiteSpace(description))
    {
      descriptionSection.BodyLines.Add(description);
    }

    var sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase)
    {
      ["Metadata"] = metadata,
      ["Description"] = descriptionSection
    };

    if (!string.IsNullOrWhiteSpace(parent))
    {
      var links = new SectionContent();
      links.KeyValues["Parent"] = parent;
      sections["Links"] = links;
    }

    if (relations is not null)
    {
      var relationSection = new SectionContent();
      foreach ((string key, List<string> values) in relations)
      {
        relationSection.NestedLists[key] = values;
      }

      sections["Relations"] = relationSection;
    }

    return new ArtifactDocument
    {
      Path = path,
      Title = title,
      Sections = sections
    };
  }
}
