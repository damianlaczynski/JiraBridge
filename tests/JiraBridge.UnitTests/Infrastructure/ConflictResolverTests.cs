using System.Net;
using System.Net.Http;
using System.Text;
using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Domain.Sync;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class ConflictResolverTests
{
  [Fact]
  public async Task ResolveAsync_WithJiraStrategy_RewritesLocalArtifactAndClearsConflict()
  {
    string repoRoot = CreateRepositoryRoot();
    RepositorySettings settings = PrepareRepository(repoRoot);
    string artifactPath = CreateArtifact(
      repoRoot,
      "story.md",
      """
      # Local story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Local body.
      """);

    using var scope = CreateScope(repoRoot);
    ConflictStore.Record(repoRoot, new ConflictRecord("SCRUM-2", Path.Combine("project-docs", "backlog", "story.md"), "pull", "Local story", "Story", "L", "R", "details"));

    var handler = new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal(HttpMethod.Get, request.Method);
      Assert.Equal("/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated", request.RequestUri!.PathAndQuery);

      return StubHttpMessageHandler.Json(
        """
        {
          "key":"SCRUM-2",
          "fields":{
            "summary":"Remote story",
            "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote body."}]}]},
            "issuetype":{"name":"Story"},
            "status":{"name":"In Progress"},
            "updated":"2026-05-01T10:15:00.000+0000",
            "issuelinks":[]
          }
        }
        """);
    });

    var resolver = new ConflictResolver(new StubJiraApiClientFactory(handler), UnexpectedMetadataRefresh());

    CommandResult result = await resolver.ResolveAsync("SCRUM-2", ConflictResolutionStrategy.Jira, CancellationToken.None);

    Assert.True(result.Success);
    string fileContent = File.ReadAllText(artifactPath);
    Assert.Contains("# Remote story", fileContent);
    Assert.Contains("Remote body.", fileContent);
    Assert.DoesNotContain("Local body.", fileContent);
    Assert.Empty(ConflictFileStore.Load(repoRoot));
  }

  [Fact]
  public async Task ResolveAsync_WithRepositoryStrategy_UpdatesRemoteIssueAndPersistsSyncMetadata()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string artifactPath = CreateArtifact(
      repoRoot,
      "story.md",
      """
      # Local story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - none

      ## Description

      Repository description.
      """);

    using var scope = CreateScope(repoRoot);
    ConflictStore.Record(repoRoot, new ConflictRecord("SCRUM-2", Path.Combine("project-docs", "backlog", "story.md"), "push", "Local story", "Story", "L", "R", "details"));

    string? putBody = null;
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      if (request.Method == HttpMethod.Get && path == "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated")
      {
        return StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-2",
            "fields":{
              "summary":"Remote story",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote description."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"Done"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """);
      }

      if (request.Method == HttpMethod.Get && path == "/rest/api/3/project/SCRUM")
      {
        return StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}""");
      }

      if (request.Method == HttpMethod.Get && path == "/rest/api/3/issuetype/project?projectId=100")
      {
        return StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}""");
      }

      if (request.Method == HttpMethod.Put && path == "/rest/api/3/issue/SCRUM-2")
      {
        putBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return new HttpResponseMessage(HttpStatusCode.NoContent);
      }

      return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") };
    });

    var resolver = new ConflictResolver(new StubJiraApiClientFactory(handler), UnexpectedMetadataRefresh());

    CommandResult result = await resolver.ResolveAsync("SCRUM-2", ConflictResolutionStrategy.Repository, CancellationToken.None);

    Assert.True(result.Success);
    Assert.NotNull(putBody);
    Assert.Contains("Local story", putBody);
    Assert.Contains("Repository description.", putBody);

    string fileContent = File.ReadAllText(artifactPath);
    Assert.Matches("(?s)Jira Last Synced Local Hash: [A-F0-9]{64}", fileContent);
    Assert.Matches("(?s)Jira Last Synced Remote Hash: [A-F0-9]{64}", fileContent);
    Assert.Empty(ConflictFileStore.Load(repoRoot));
  }

  [Fact]
  public void BuildMergedDescription_WhenBothSidesDiffer_AddsConflictMarkers()
  {
    ArtifactDocument document = new()
    {
      Path = Path.Combine("C:", "repo", "story.md"),
      Title = "Story",
      Sections = new Dictionary<string, JiraBridge.Domain.Artifacts.SectionContent>(StringComparer.OrdinalIgnoreCase)
      {
        ["Description"] = new()
        {
          BodyLines = { "Local text" }
        }
      }
    };

    string merged = ConflictResolver.BuildMergedDescription(
      document,
      new JiraRemoteIssue("SCRUM-2", "Story", "To Do", "Story", "Remote text", DateTimeOffset.UtcNow, null, []));

    Assert.Contains("<<<<<<< REPOSITORY", merged);
    Assert.Contains("Local text", merged);
    Assert.Contains("Remote text", merged);
    Assert.Contains(">>>>>>> JIRA", merged);
  }

  private static IRepositoryMetadataRefresher UnexpectedMetadataRefresh() =>
    new FakeRepositoryMetadataRefresher((_, _, _) =>
      Task.FromException<RepositoryJiraConfiguration>(
        new InvalidOperationException("Unexpected metadata refresh in unit test.")));

  private static TestProcessScope CreateScope(string repoRoot)
  {
    var scope = new TestProcessScope(
      repoRoot,
      "JIRABRIDGE_JIRA_BASE_URL",
      "JIRABRIDGE_JIRA_EMAIL",
      "JIRABRIDGE_JIRA_API_TOKEN");
    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_BASE_URL", "https://example.atlassian.net");
    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_EMAIL", "user@example.com");
    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_API_TOKEN", "token");
    return scope;
  }

  private static RepositorySettings PrepareRepository(string repoRoot)
  {
    RepositorySettings settings = RepositorySettingsStore.CreateDefault("SCRUM");
    RepositorySettingsStore.Save(repoRoot, settings);
    RepositoryJiraConfigurationStore.Save(
      repoRoot,
      settings,
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        [],
        SprintProjectionCached: true));

    Directory.CreateDirectory(Path.Combine(repoRoot, "project-docs", "backlog"));
    return settings;
  }

  private static string CreateRepositoryRoot()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
    return repoRoot;
  }

  private static string CreateArtifact(string repoRoot, string fileName, string content)
  {
    string path = Path.Combine(repoRoot, "project-docs", "backlog", fileName);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.UTF8);
    return path;
  }

  private sealed class StubJiraApiClientFactory(StubHttpMessageHandler handler) : IJiraApiClientFactory
  {
    public JiraApiClient Create(JiraSettings settings) =>
      new(new HttpClient(handler) { BaseAddress = settings.BaseUri });
  }
}
