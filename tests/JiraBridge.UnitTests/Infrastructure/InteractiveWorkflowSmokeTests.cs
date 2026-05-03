using System.Net;
using System.Net.Http;
using System.Text;
using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Sync;
using JiraBridge.Host.Terminal;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class InteractiveWorkflowSmokeTests
{
  [Fact]
  public async Task ConfigurePushConflictAndResolveRepositoryFlow_WorksEndToEnd()
  {
    string repoRoot = CreateRepositoryRoot();
    using var scope = CreateScope(repoRoot);

    string? updateBody = null;
    var progress = new OperationProgressTracker();
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      return path switch
      {
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        "/rest/api/3/issueLinkType" => StubHttpMessageHandler.Json("""{"issueLinkTypes":[{"id":"10","name":"Blocks","inward":"is blocked by","outward":"blocks"}]}"""),
        "/rest/api/3/project/SCRUM/statuses" => StubHttpMessageHandler.Json(
          """
          [
            {
              "id":"1",
              "name":"Story",
              "statuses":[
                {"id":"11","name":"To Do","statusCategory":{"key":"new"}},
                {"id":"12","name":"In Progress","statusCategory":{"key":"indeterminate"}},
                {"id":"13","name":"Done","statusCategory":{"key":"done"}}
              ]
            }
          ]
          """),
        "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated" when request.Method == HttpMethod.Get => StubHttpMessageHandler.Json(
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
          """),
        "/rest/api/3/issue/SCRUM-2" when request.Method == HttpMethod.Put => CaptureNoContent(request, captured => updateBody = captured),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    var factory = new StubJiraApiClientFactory(handler);
    var refresher = new RepositoryMetadataRefresher(factory, progress);
    var bootstrapper = new RepositoryBootstrapper(refresher, progress);
    var executor = new SyncExecutor(factory, refresher, progress);
    var resolver = new ConflictResolver(factory, refresher);

    CommandResult configure = await bootstrapper.ConfigureAsync("SCRUM", CancellationToken.None);

    Assert.True(configure.Success);
    Assert.Contains("Repository configuration saved", configure.Message);
    Assert.True(File.Exists(Path.Combine(repoRoot, ".jirabridge", "settings.json")));
    Assert.True(File.Exists(Path.Combine(repoRoot, ".jirabridge", "jira-project.json")));

    string artifactPath = CreateArtifact(
      repoRoot,
      "story.md",
      """
      # Local story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: OLDLOCAL
      - Jira Last Synced Remote Hash: OLDREMOTE

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Repository description.
      """);

    CommandResult push = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(push.Success);
    ConflictRecord conflict = Assert.Single(ConflictFileStore.Load(repoRoot));
    Assert.Equal("SCRUM-2", conflict.IssueKey);
    Assert.Contains("Repository Description vs Jira Description", conflict.Details);

    CommandResult resolve = await resolver.ResolveAsync("SCRUM-2", ConflictResolutionStrategy.Repository, CancellationToken.None);

    Assert.True(resolve.Success);
    Assert.NotNull(updateBody);
    Assert.Contains("Local story", updateBody);
    Assert.Contains("Repository description.", updateBody);
    Assert.Empty(ConflictFileStore.Load(repoRoot));

    string fileContent = File.ReadAllText(artifactPath);
    Assert.Matches("(?s)Jira Last Synced Local Hash: [A-F0-9]{64}", fileContent);
    Assert.Matches("(?s)Jira Last Synced Remote Hash: [A-F0-9]{64}", fileContent);

    OperationProgressState snapshot = progress.GetSnapshot();
    Assert.False(snapshot.IsActive);
    Assert.Contains(snapshot.Timeline, line => line.Contains("No push actions were executed", StringComparison.Ordinal));
  }

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

  private static HttpResponseMessage CaptureNoContent(HttpRequestMessage request, Action<string> capture)
  {
    capture(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    return new HttpResponseMessage(HttpStatusCode.NoContent);
  }

  private sealed class StubJiraApiClientFactory(StubHttpMessageHandler handler) : IJiraApiClientFactory
  {
    public JiraApiClient Create(JiraSettings settings) =>
      new(new HttpClient(handler) { BaseAddress = settings.BaseUri });
  }
}
