using System.Net;
using System.Net.Http;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class JiraApiClientTests
{
  [Fact]
  public async Task GetProjectIssueTypesAsync_CachesProjectMetadataRequests()
  {
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string pathAndQuery = request.RequestUri!.PathAndQuery;

      return pathAndQuery switch
      {
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    var first = await client.GetProjectIssueTypesAsync("SCRUM", CancellationToken.None);
    var second = await client.GetProjectIssueTypesAsync("SCRUM", CancellationToken.None);

    Assert.Single(first);
    Assert.Single(second);
    Assert.Equal(2, handler.Requests.Count);
  }

  [Fact]
  public async Task GetProjectInfoAsync_WhenApiFails_IncludesStatusAndBodyInException()
  {
    var handler = new StubHttpMessageHandler((_, _) =>
      new HttpResponseMessage(HttpStatusCode.BadRequest)
      {
        ReasonPhrase = "Bad Request",
        Content = new StringContent("""{"error":"boom"}""")
      });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      client.GetProjectInfoAsync("SCRUM", CancellationToken.None));

    Assert.Contains("HTTP 400", ex.Message);
    Assert.Contains("boom", ex.Message);
  }

  [Fact]
  public async Task GetIssueAsync_ParsesMarkdownParentLinksAndUpdatedTimestamp()
  {
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      Assert.Equal("/rest/api/3/issue/SCRUM-10?fields=summary,description,issuetype,status,parent,issuelinks,updated", request.RequestUri!.PathAndQuery);

      return StubHttpMessageHandler.Json(
        """
        {
          "key":"SCRUM-10",
          "fields":{
            "summary":"Improve parser",
            "description":{"type":"doc","version":1,"content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Scope"}]},{"type":"paragraph","content":[{"type":"text","text":"Support "},{"type":"text","text":"ADF","marks":[{"type":"strong"}]}]}]},
            "issuetype":{"name":"Story"},
            "status":{"name":"In Progress"},
            "parent":{"key":"SCRUM-1"},
            "updated":"2026-05-01T12:30:00.000+0000",
            "issuelinks":[
              {"type":{"name":"Blocks"},"outwardIssue":{"key":"SCRUM-11"}},
              {"type":{"name":"Relates"},"inwardIssue":{"key":"SCRUM-12"}}
            ]
          }
        }
        """);
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    JiraRemoteIssue issue = await client.GetIssueAsync("SCRUM-10", CancellationToken.None);

    Assert.Equal("SCRUM-10", issue.IssueKey);
    Assert.Equal("Story", issue.IssueType);
    Assert.Equal("In Progress", issue.Status);
    Assert.Equal("Improve parser", issue.Summary);
    Assert.Contains("## Scope", issue.Description);
    Assert.Contains("**ADF**", issue.Description);
    Assert.Equal("SCRUM-1", issue.ParentIssueKey);
    Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 30, 0, TimeSpan.Zero), issue.UpdatedAt);
    Assert.Collection(
      issue.Links,
      first =>
      {
        Assert.Equal("Blocks", first.LinkType);
        Assert.Null(first.InwardIssueKey);
        Assert.Equal("SCRUM-11", first.OutwardIssueKey);
      },
      second =>
      {
        Assert.Equal("Relates", second.LinkType);
        Assert.Equal("SCRUM-12", second.InwardIssueKey);
        Assert.Null(second.OutwardIssueKey);
      });
  }

  [Fact]
  public async Task SearchProjectIssuesAsync_FollowsPaginationAndFallsBackForInvalidUpdatedTimestamp()
  {
    int requestCount = 0;
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      requestCount++;
      string pathAndQuery = request.RequestUri!.PathAndQuery;

      return pathAndQuery switch
      {
        "/rest/api/3/search/jql" when requestCount == 1 => StubHttpMessageHandler.Json(
          """
          {
            "issues":[
              {
                "key":"SCRUM-1",
                "fields":{
                  "summary":"First",
                  "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"One"}]}]},
                  "issuetype":{"name":"Task"},
                  "status":{"name":"To Do"},
                  "updated":"bad-date",
                  "issuelinks":[]
                }
              }
            ],
            "isLast":false,
            "nextPageToken":"page-2"
          }
          """),
        "/rest/api/3/search/jql" => StubHttpMessageHandler.Json(
          """
          {
            "issues":[
              {
                "key":"SCRUM-2",
                "fields":{
                  "summary":"Second",
                  "description":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Two"}]}]},
                  "issuetype":{"name":"Bug"},
                  "status":{"name":"Done"},
                  "updated":"2026-05-01T10:15:00.000+0000",
                  "issuelinks":[]
                }
              }
            ],
            "isLast":true
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    IReadOnlyList<JiraRemoteIssue> issues = await client.SearchProjectIssuesAsync("SCRUM", CancellationToken.None);

    Assert.Equal(2, issues.Count);
    Assert.Equal("SCRUM-1", issues[0].IssueKey);
    Assert.Equal(DateTimeOffset.MinValue, issues[0].UpdatedAt);
    Assert.Equal("SCRUM-2", issues[1].IssueKey);
    Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 15, 0, TimeSpan.Zero), issues[1].UpdatedAt);
  }

  [Fact]
  public async Task CreateIssueAsync_SendsIssuePayloadAndReturnsCreatedKey()
  {
    var payload = new JiraIssuePayload(
      "SCRUM",
      "Story",
      "Created from repo",
      "Description",
      null,
      null,
      null,
      new Dictionary<string, IReadOnlyList<string>>());

    string? body = null;
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string pathAndQuery = request.RequestUri!.PathAndQuery;

      return pathAndQuery switch
      {
        "/rest/api/3/project/SCRUM" => StubHttpMessageHandler.Json("""{"id":"100","key":"SCRUM","name":"Scrum"}"""),
        "/rest/api/3/issuetype/project?projectId=100" => StubHttpMessageHandler.Json("""{"issueTypes":[{"id":"1","name":"Story","subtask":false}]}"""),
        "/rest/api/3/issue" => CreateIssueResponse(request, captured => body = captured),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    string createdKey = await client.CreateIssueAsync(payload, CancellationToken.None);

    Assert.Equal("SCRUM-77", createdKey);
    Assert.NotNull(body);
    Assert.Contains("Created from repo", body);
    Assert.Contains("\"id\":\"1\"", body);
  }

  [Fact]
  public async Task EnsureIssueLinkAsync_SkipsCreateWhenLinkAlreadyExists()
  {
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string pathAndQuery = request.RequestUri!.PathAndQuery;

      return pathAndQuery switch
      {
        "/rest/api/3/issue/SCRUM-1?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-1",
            "fields":{
              "summary":"One",
              "description":{"type":"doc","version":1,"content":[]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[
                {"type":{"name":"Blocks"},"outwardIssue":{"key":"SCRUM-1"},"inwardIssue":{"key":"SCRUM-2"}}
              ]
            }
          }
          """),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    await client.EnsureIssueLinkAsync("Blocks", "SCRUM-1", "SCRUM-2", CancellationToken.None);

    Assert.Single(handler.Requests);
  }

  [Fact]
  public async Task EnsureIssueLinkAsync_CreatesLinkWhenMissing()
  {
    string? body = null;
    var handler = new StubHttpMessageHandler((request, _) =>
    {
      string pathAndQuery = request.RequestUri!.PathAndQuery;

      return pathAndQuery switch
      {
        "/rest/api/3/issue/SCRUM-1?fields=summary,description,issuetype,status,parent,issuelinks,updated" => StubHttpMessageHandler.Json(
          """
          {
            "key":"SCRUM-1",
            "fields":{
              "summary":"One",
              "description":{"type":"doc","version":1,"content":[]},
              "issuetype":{"name":"Story"},
              "status":{"name":"To Do"},
              "updated":"2026-05-01T10:15:00.000+0000",
              "issuelinks":[]
            }
          }
          """),
        "/rest/api/3/issueLink" => CreateNoContentResponse(request, captured => body = captured),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") }
      };
    });

    using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net") };
    using var client = new JiraApiClient(httpClient);

    await client.EnsureIssueLinkAsync("Blocks", "SCRUM-1", "SCRUM-2", CancellationToken.None);

    Assert.Equal(2, handler.Requests.Count);
    Assert.NotNull(body);
    Assert.Contains("\"name\":\"Blocks\"", body);
    Assert.Contains("\"key\":\"SCRUM-1\"", body);
    Assert.Contains("\"key\":\"SCRUM-2\"", body);
  }

  private static HttpResponseMessage CreateIssueResponse(HttpRequestMessage request, Action<string> capture)
  {
    capture(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    return StubHttpMessageHandler.Json("""{"key":"SCRUM-77"}""");
  }

  private static HttpResponseMessage CreateNoContentResponse(HttpRequestMessage request, Action<string> capture)
  {
    capture(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    return new HttpResponseMessage(HttpStatusCode.NoContent);
  }
}
