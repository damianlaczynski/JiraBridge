using System.Net;
using System.Net.Http;
using System.Text;
using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Configuration;
using JiraBridge.Domain.Sync;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Parsing;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;
using JiraBridge.Host.Terminal;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class SyncExecutorTests
{
  [Fact]
  public async Task PullAsync_ImportsNewArtifactFromJira()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("/rest/api/3/search/jql", request.RequestUri!.PathAndQuery);

      return StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-10",
              "fields":{
                "summary":"Imported story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Imported body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"To Do"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """);
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    string importedPath = Path.Combine(repoRoot, "docs", "jira-bridge", "backlog", "SCRUM-10", "SCRUM-10.md");
    Assert.True(File.Exists(importedPath));
    string importedContent = File.ReadAllText(importedPath);
    Assert.Contains("# Imported story", importedContent);
    Assert.Contains("Imported body.", importedContent);
    Assert.Matches("(?s)Jira Last Synced Local Hash: [A-F0-9]{64}", importedContent);
    Assert.Matches("(?s)Jira Last Synced Remote Hash: [A-F0-9]{64}", importedContent);
  }

  [Fact]
  public async Task PullAsync_UpdatesExistingArtifactWhenOnlyRemoteChanged()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "SCRUM-2", "SCRUM-2.md"),
      """
      # Existing story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: B0B787AFB1D6B7F1FB1648B5A113D96F4C1A7A16B5E9B0B81967EB663D5C2F42
      - Jira Last Synced Remote Hash: OLDREMOTE

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Existing body.
      """);
    StampCurrentLocalHash(artifactPath, "SCRUM-2", "OLDREMOTE");

    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);
      return StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-2",
              "fields":{
                "summary":"Updated remote story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote replacement body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"Done"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """);
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    string content = File.ReadAllText(artifactPath);
    Assert.Contains("# Updated remote story", content);
    Assert.Contains("Remote replacement body.", content);
    Assert.DoesNotContain("Existing body.", content);
    Assert.Empty(ConflictFileStore.Load(repoRoot));
  }

  [Fact]
  public async Task PullAsync_WhenSprintMappingEnabled_ImportsArtifactIntoSprintDirectory()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(
      repoRoot,
      new RepositorySettings(1, "SCRUM", "docs/jira-bridge", ".jirabridge/project-metadata.json", SprintMappingEnabled: true),
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        [],
        SprintFieldId: "customfield_10020",
        Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]));
    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);

      return StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-10",
              "fields":{
                "summary":"Imported story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Imported body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"To Do"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "customfield_10020":{"id":24,"name":"Sprint 24","state":"active","boardId":7},
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """);
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    string importedPath = Path.Combine(repoRoot, "docs", "jira-bridge", "sprint-sprint-24", "SCRUM-10", "SCRUM-10.md");
    Assert.True(File.Exists(importedPath));
  }

  [Fact]
  public async Task PullAsync_WhenSprintChanges_RemapsArtifactPath()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(
      repoRoot,
      new RepositorySettings(1, "SCRUM", "docs/jira-bridge", ".jirabridge/project-metadata.json", SprintMappingEnabled: true),
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        [],
        SprintFieldId: "customfield_10020",
        Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]));

    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "story.md"),
      """
      # Existing story

      ## Description

      Existing body.

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: B0B787AFB1D6B7F1FB1648B5A113D96F4C1A7A16B5E9B0B81967EB663D5C2F42
      - Jira Last Synced Remote Hash: OLDREMOTE
      """);
    StampCurrentLocalHash(artifactPath, "SCRUM-2", "OLDREMOTE");

    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
      StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-2",
              "fields":{
                "summary":"Existing story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Existing body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"Done"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "customfield_10020":{"id":24,"name":"Sprint 24","state":"active","boardId":7},
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """))), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    string movedPath = Path.Combine(repoRoot, "docs", "jira-bridge", "sprint-sprint-24", "SCRUM-2", "SCRUM-2.md");
    Assert.True(File.Exists(movedPath));
    Assert.False(File.Exists(artifactPath));
  }

  [Fact]
  public async Task PullAsync_WhenArtifactRelocated_RemovesEmptyKeyDirectories()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(
      repoRoot,
      new RepositorySettings(1, "SCRUM", "docs/jira-bridge", ".jirabridge/project-metadata.json", SprintMappingEnabled: true),
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        [],
        SprintFieldId: "customfield_10020",
        Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]));

    string oldKeyDirectory = Path.Combine(repoRoot, "docs", "jira-bridge", "backlog", "SCRUM-2");
    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "SCRUM-2", "SCRUM-2.md"),
      """
      # Existing story

      ## Description

      Existing body.

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: B0B787AFB1D6B7F1FB1648B5A113D96F4C1A7A16B5E9B0B81967EB663D5C2F42
      - Jira Last Synced Remote Hash: OLDREMOTE
      """);
    StampCurrentLocalHash(artifactPath, "SCRUM-2", "OLDREMOTE");

    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
      StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-2",
              "fields":{
                "summary":"Existing story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Existing body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"Done"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "customfield_10020":{"id":24,"name":"Sprint 24","state":"active","boardId":7},
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """))), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    Assert.False(Directory.Exists(oldKeyDirectory));
  }

  [Fact]
  public async Task PushAsync_WhenSprintMappingEnabled_SendsSprintField()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(
      repoRoot,
      new RepositorySettings(1, "SCRUM", "docs/jira-bridge", ".jirabridge/project-metadata.json", SprintMappingEnabled: true),
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        [],
        SprintFieldId: "customfield_10020",
        Sprints: [new JiraSprintInfo(24, "Sprint 24", "active", 7)]));

    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("sprint-sprint-24", "SCRUM-2", "SCRUM-2.md"),
      """
      # Updated locally

      ## Description

      Local push body.

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - none

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: OLDLOCAL
      - Jira Last Synced Remote Hash: MATCHINGREMOTE
      """);
    string matchingRemoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(
      new JiraRemoteIssue(
        "SCRUM-2",
        "Story",
        "To Do",
        "Remote old title",
        "Remote old body.",
        DateTimeOffset.UtcNow,
        new JiraSprintInfo(24, "Sprint 24", "active", 7),
        null,
        []));
    File.WriteAllText(
      artifactPath,
      File.ReadAllText(artifactPath).Replace("MATCHINGREMOTE", matchingRemoteHash, StringComparison.Ordinal),
      Encoding.UTF8);

    using var scope = CreateScope(repoRoot);
    string? updateBody = null;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      return path switch
      {
        "/rest/api/3/field" => StubHttpMessageHandler.Json("""[{"id":"customfield_10020","schema":{"custom":"com.pyxis.greenhopper.jira:gh-sprint"}}]"""),
        "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated,customfield_10020" when request.Method == HttpMethod.Get => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-2",
            "fields":{
              "summary":"Remote old title",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote old body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "customfield_10020":{"id":24,"name":"Sprint 24","state":"active","boardId":7},
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        "/rest/api/3/issue/SCRUM-2" when request.Method == HttpMethod.Put => CaptureNoContent(request, captured => updateBody = captured),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(result.Success);
    Assert.NotNull(updateBody);
    Assert.Contains("\"customfield_10020\":24", updateBody);
  }

  [Fact]
  public async Task PullAsync_WhenLocalAndRemoteChanged_RecordsConflictWithoutOverwritingFile()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "story.md"),
      """
      # Local changed story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: OLDLOCAL
      - Jira Last Synced Remote Hash: OLDREMOTE

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - none

      ## Description

      Local modified body.
      """);

    using var scope = CreateScope(repoRoot);

    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
      StubHttpMessageHandler.Json(
        """
        {
          "issues":[
            {
              "key":"SCRUM-2",
              "fields":{
                "summary":"Remote changed story",
                "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote modified body."}]}]},
                "issuetype":{"name":"Story"},
                "status":{"name":"Done"},
                "updated":"2026-05-01T10:15:00.000+0000",
                "issuelinks":[]
              }
            }
          ],
          "isLast":true
        }
        """))), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PullAsync(CancellationToken.None);

    Assert.True(result.Success);
    string content = File.ReadAllText(artifactPath);
    Assert.Contains("Local modified body.", content);
    Assert.DoesNotContain("Remote modified body.", content);

    ConflictRecord conflict = Assert.Single(ConflictFileStore.Load(repoRoot));
    Assert.Equal("SCRUM-2", conflict.IssueKey);
    Assert.Equal("pull", conflict.Operation);
    Assert.Contains("Repository Description vs Jira Description", conflict.Details);
  }

  [Fact]
  public async Task PushAsync_CreatesNewIssueAndPersistsCreatedKey()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string artifactPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "new-story.md"),
      """
      # New story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key:
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Create me.
      """);

    using var scope = CreateScope(repoRoot);
    string? createBody = null;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      return path switch
      {
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        "/rest/api/3/issue" when request.Method == HttpMethod.Post => CaptureJson(request, captured => createBody = captured, """{"key":"SCRUM-88"}"""),
        "/rest/api/3/issue/SCRUM-88?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-88",
            "fields":{
              "summary":"New story",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Create me."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(result.Success);
    Assert.NotNull(createBody);
    Assert.Contains("New story", createBody);
    string content = File.ReadAllText(artifactPath);
    Assert.Contains("- Jira Issue Key: SCRUM-88", content);
    Assert.Matches("(?s)Jira Last Synced Local Hash: [A-F0-9]{64}", content);
    Assert.Matches("(?s)Jira Last Synced Remote Hash: [A-F0-9]{64}", content);
  }

  [Fact]
  public async Task PushAsync_UpdatesExistingIssueAndCreatesLinks()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string dependencyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "dependency.md"),
      """
      # Dependency

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-3
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Dependency body.
      """);
    StampCurrentHashes(dependencyPath, new JiraRemoteIssue("SCRUM-3", "Story", "To Do", "Dependency", "Dependency body.", DateTimeOffset.UtcNow, null, []));

    string storyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "story.md"),
      """
      # Updated locally

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-2
      - Jira Last Synced Local Hash: OLDLOCAL
      - Jira Last Synced Remote Hash: MATCHINGREMOTE

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - dependency.md

      ## Description

      Local push body.
      """);
    string matchingRemoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(
      new JiraRemoteIssue("SCRUM-2", "Story", "To Do", "Remote old title", "Remote old body.", DateTimeOffset.UtcNow, null, []));
    File.WriteAllText(
      storyPath,
      File.ReadAllText(storyPath).Replace("MATCHINGREMOTE", matchingRemoteHash, StringComparison.Ordinal),
      Encoding.UTF8);

    using var scope = CreateScope(repoRoot);
    string? updateBody = null;
    string? linkBody = null;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      return path switch
      {
        "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated" when request.Method == HttpMethod.Get => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-2",
            "fields":{
              "summary":"Remote old title",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote old body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        "/rest/api/3/issue/SCRUM-2" when request.Method == HttpMethod.Put => CaptureNoContent(request, captured => updateBody = captured),
        "/rest/api/3/issueLink" when request.Method == HttpMethod.Post => CaptureNoContent(request, captured => linkBody = captured),
        "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-2",
            "fields":{
              "summary":"Updated locally",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Local push body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"In Progress"},
              "updated":"2026-05-02T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issue/SCRUM-3?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-3",
            "fields":{
              "summary":"Dependency",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Dependency body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(result.Success);
    Assert.NotNull(updateBody);
    Assert.Contains("Updated locally", updateBody);
    Assert.NotNull(linkBody);
    Assert.Contains("\"name\":\"Blocks\"", linkBody);
    Assert.Contains("\"key\":\"SCRUM-2\"", linkBody);
    Assert.Contains("\"key\":\"SCRUM-3\"", linkBody);
  }

  [Fact]
  public async Task PushAsync_DryRun_SummarizesNestedHierarchyWithoutWritingToJira()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);

    string epicPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "scrum-1-platform.md"),
      """
      # Platform epic

      ## Metadata

      - Issue Type: Epic
      - Jira Issue Key: SCRUM-1
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Platform epic body.
      """);
    StampCurrentHashes(epicPath, new JiraRemoteIssue("SCRUM-1", "Epic", "To Do", "Platform epic", "Platform epic body.", DateTimeOffset.UtcNow, null, []));

    string dependencyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "shared", "dependency.md"),
      """
      # Shared dependency

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-9
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Shared dependency body.
      """);
    StampCurrentHashes(dependencyPath, new JiraRemoteIssue("SCRUM-9", "Story", "To Do", "Shared dependency", "Shared dependency body.", DateTimeOffset.UtcNow, null, []));

    CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "story", "new-story.md"),
      """
      # New nested story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key:
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: ../scrum-1-platform.md

      ## Relations

      ### Blocks

      - ../../shared/dependency.md

      ## Description

      Story body.
      """);

    string taskPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "story", "tasks", "task.md"),
      """
      # Nested task

      ## Metadata

      - Issue Type: Task
      - Jira Issue Key:
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: ../new-story.md

      ## Relations

      ### Relates

      - ../../../shared/dependency.md

      ## Description

      Task body.
      """);

    using var scope = CreateScope(repoRoot);
    bool attemptedWrite = false;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;
      if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
      {
        attemptedWrite = true;
      }

      return path switch
      {
        "/rest/api/3/issue/SCRUM-1?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-1",
            "fields":{
              "summary":"Platform epic",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Platform epic body."}]}]},
              "issuetype":{"name":"Epic"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issue/SCRUM-9?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-9",
            "fields":{
              "summary":"Shared dependency",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Shared dependency body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: true, CancellationToken.None);

    Assert.True(result.Success);
    Assert.Contains("Push dry-run complete. Actionable artifacts: 2. Conflicts: 0.", result.Message);
    Assert.False(attemptedWrite);
    Assert.Contains(result.Details!, line => line.Contains("Creates: 2", StringComparison.Ordinal));
    Assert.Contains(result.Details!, line => line.Contains("Unchanged: 2", StringComparison.Ordinal));
    Assert.Contains(result.Details!, line => line.Contains("Relationship actions:", StringComparison.Ordinal));
    Assert.Contains(result.Details!, line => line.Contains("Dry-run preview mode", StringComparison.Ordinal));
    Assert.Empty(ConflictFileStore.Load(repoRoot));
    Assert.Contains("- Jira Issue Key:", File.ReadAllText(taskPath));
    Assert.DoesNotContain("SCRUM-10", File.ReadAllText(taskPath));
  }

  [Fact]
  public async Task PushAsync_CreatesNestedHierarchyAndLinksMultiLevelRelations()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);

    string epicPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "scrum-1-platform.md"),
      """
      # Platform epic

      ## Metadata

      - Issue Type: Epic
      - Jira Issue Key: SCRUM-1
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - ../shared/dependency.md

      ## Description

      Platform epic body.
      """);
    StampCurrentHashes(epicPath, new JiraRemoteIssue("SCRUM-1", "Epic", "To Do", "Platform epic", "Platform epic body.", DateTimeOffset.UtcNow, null, []));

    string dependencyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "shared", "dependency.md"),
      """
      # Shared dependency

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key: SCRUM-9
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: none

      ## Relations

      ### Relates

      - none

      ## Description

      Shared dependency body.
      """);
    StampCurrentHashes(dependencyPath, new JiraRemoteIssue("SCRUM-9", "Story", "To Do", "Shared dependency", "Shared dependency body.", DateTimeOffset.UtcNow, null, []));

    string storyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "story", "new-story.md"),
      """
      # New nested story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key:
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: ../scrum-1-platform.md

      ## Relations

      ### Relates

      - none

      ## Description

      Story body.
      """);

    string taskPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "epic", "story", "tasks", "task.md"),
      """
      # Nested task

      ## Metadata

      - Issue Type: Task
      - Jira Issue Key:
      - Jira Last Synced Local Hash:
      - Jira Last Synced Remote Hash:

      ## Links

      - Parent: ../new-story.md

      ## Relations

      ### Relates

      - none

      ## Description

      Task body.
      """);

    using var scope = CreateScope(repoRoot);
    var createBodies = new List<string>();
    string? linkBody = null;
    int createCounter = 0;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      string path = request.RequestUri!.PathAndQuery;

      return path switch
      {
        "/rest/api/3/issue/SCRUM-1?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-1",
            "fields":{
              "summary":"Platform epic",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Platform epic body."}]}]},
              "issuetype":{"name":"Epic"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issue/SCRUM-9?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-9",
            "fields":{
              "summary":"Shared dependency",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Shared dependency body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false},{"id":"2","name":"Task","subtask":false},{"id":"3","name":"Epic","subtask":false}]}"""),
        "/rest/api/3/issue" when request.Method == HttpMethod.Post => CaptureJson(
          request,
          captured =>
          {
            createBodies.Add(captured);
            createCounter++;
          },
          createCounter == 0 ? """{"key":"SCRUM-101"}""" : """{"key":"SCRUM-102"}"""),
        "/rest/api/3/issue/SCRUM-101?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-101",
            "fields":{
              "summary":"New nested story",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Story body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"In Progress"},
              "updated":"2026-05-02T10:15:00.000+0000",
              "parent":{"key":"SCRUM-1"},
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issue/SCRUM-102?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-102",
            "fields":{
              "summary":"Nested task",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Task body."}]}]},
              "issuetype":{"name":"Task"},
              "status":{"name":"To Do"},
              "updated":"2026-05-02T10:16:00.000+0000",
              "parent":{"key":"SCRUM-101"},
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issueLink" when request.Method == HttpMethod.Post => CaptureNoContent(request, captured => linkBody = captured),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(result.Success);
    Assert.Equal(2, createBodies.Count);
    Assert.Contains("\"key\":\"SCRUM-1\"", createBodies[0]);
    Assert.Contains("\"key\":\"SCRUM-101\"", createBodies[1]);
    Assert.Contains("\"summary\":\"Nested task\"", createBodies[1]);
    Assert.Contains("- Jira Issue Key: SCRUM-101", File.ReadAllText(storyPath));
    Assert.Contains("- Jira Issue Key: SCRUM-102", File.ReadAllText(taskPath));
    Assert.Contains(result.Details!, line => line.Contains("Creates: 2", StringComparison.Ordinal));
    Assert.Contains(result.Details!, line => line.Contains("Relationship actions:", StringComparison.Ordinal));
  }

  [Fact]
  public async Task PushAsync_WhenLocalAndRemoteChanged_RecordsConflictWithoutUpdatingJira()
  {
    string repoRoot = CreateRepositoryRoot();
    PrepareRepository(repoRoot);
    string storyPath = CreateArtifact(
      repoRoot,
      Path.Combine("backlog", "story.md"),
      """
      # Local push title

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

      Local push body.
      """);

    using var scope = CreateScope(repoRoot);
    bool attemptedWrite = false;
    var executor = new SyncExecutor(new StubJiraApiClientFactory(new StubHttpMessageHandler((request, _) =>
    {
      if ((request.Method == HttpMethod.Put || request.Method == HttpMethod.Post) &&
          (request.RequestUri!.PathAndQuery == "/rest/api/3/issue" || request.RequestUri.PathAndQuery == "/rest/api/3/issueLink"))
      {
        attemptedWrite = true;
      }

      return request.RequestUri!.PathAndQuery switch
      {
        "/rest/api/3/issue/SCRUM-2?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-2",
            "fields":{
              "summary":"Remote title",
              "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Remote body."}]}]},
              "issuetype":{"name":"Story"},
              "status":{"name":"Done"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    })), new LocalFixtureMetadataRefresher(), new OperationProgressTracker());

    CommandResult result = await executor.PushAsync(dryRun: false, CancellationToken.None);

    Assert.True(result.Success);
    Assert.False(attemptedWrite);
    ConflictRecord conflict = Assert.Single(ConflictFileStore.Load(repoRoot));
    Assert.Equal("SCRUM-2", conflict.IssueKey);
    Assert.Equal("push", conflict.Operation);
    Assert.Contains("Repository Description vs Jira Description", conflict.Details);
    Assert.Contains("Local push body.", File.ReadAllText(storyPath));
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

  private static void PrepareRepository(string repoRoot)
  {
    PrepareRepository(
      repoRoot,
      RepositorySettingsStore.CreateDefault("SCRUM"),
      new RepositoryJiraConfiguration(
        "SCRUM",
        "100",
        "Scrum",
        "https://example.atlassian.net",
        [new JiraProjectIssueType("1", "Story", false)],
        [],
        []));
  }

  private static void PrepareRepository(string repoRoot, RepositorySettings settings, RepositoryJiraConfiguration configuration)
  {
    RepositorySettingsStore.Save(repoRoot, settings);
    RepositoryJiraConfigurationStore.Save(repoRoot, settings, configuration);

    Directory.CreateDirectory(Path.Combine(repoRoot, "docs", "jira-bridge"));
  }

  private static string CreateRepositoryRoot()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
    return repoRoot;
  }

  private static string CreateArtifact(string repoRoot, string relativePath, string content)
  {
    string path = Path.Combine(repoRoot, "docs", "jira-bridge", relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.UTF8);
    return path;
  }

  private static void StampCurrentLocalHash(string artifactPath, string issueKey, string remoteHash)
  {
    JiraBridge.Domain.Artifacts.ArtifactDocument document = ArtifactMarkdownParser.TryParse(artifactPath, out List<string> errors)
      ?? throw new InvalidOperationException(string.Join("; ", errors));
    string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(document);
    ArtifactFileUpdater.WriteSyncMetadata(artifactPath, issueKey, localHash, remoteHash);
  }

  private static void StampCurrentHashes(string artifactPath, JiraRemoteIssue remoteIssue)
  {
    JiraBridge.Domain.Artifacts.ArtifactDocument document = ArtifactMarkdownParser.TryParse(artifactPath, out List<string> errors)
      ?? throw new InvalidOperationException(string.Join("; ", errors));
    string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(document);
    string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
    ArtifactFileUpdater.WriteSyncMetadata(artifactPath, remoteIssue.IssueKey, localHash, remoteHash);
  }

  private static HttpResponseMessage CaptureJson(HttpRequestMessage request, Action<string> capture, string json)
  {
    capture(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    return StubHttpMessageHandler.Json(json);
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
