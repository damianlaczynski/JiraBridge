using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Parsing;

namespace JiraBridge.Infrastructure.Jira;

public sealed class JiraApiClient : IDisposable
{
  private readonly HttpClient httpClient;
  private readonly bool ownsHttpClient;
  private readonly Dictionary<string, string> projectIdCache = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, IReadOnlyList<JiraProjectIssueType>> projectIssueTypesCache = new(StringComparer.OrdinalIgnoreCase);

  public JiraApiClient(JiraSettings settings)
    : this(CreateConfiguredHttpClient(settings), ownsHttpClient: true)
  {
  }

  public JiraApiClient(HttpClient httpClient, bool ownsHttpClient = false)
  {
    this.httpClient = httpClient;
    this.ownsHttpClient = ownsHttpClient;
  }

  public async Task<JiraProjectInfo> GetProjectInfoAsync(string projectKey, CancellationToken cancellationToken)
  {
    using var response = await httpClient.GetAsync(
      $"/rest/api/3/project/{Uri.EscapeDataString(projectKey)}",
      cancellationToken);

    await EnsureSuccessAsync(response, $"load project {projectKey}", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
    return new JiraProjectInfo(
      Id: json.RootElement.GetProperty("id").GetString()
          ?? throw new InvalidOperationException($"Jira project '{projectKey}' response did not contain an id."),
      Key: json.RootElement.GetProperty("key").GetString()
          ?? throw new InvalidOperationException($"Jira project '{projectKey}' response did not contain a key."),
      Name: json.RootElement.GetProperty("name").GetString()
          ?? throw new InvalidOperationException($"Jira project '{projectKey}' response did not contain a name."));
  }

  public async Task<IReadOnlyList<JiraProjectIssueType>> GetProjectIssueTypesAsync(string projectKey, CancellationToken cancellationToken)
  {
    if (projectIssueTypesCache.TryGetValue(projectKey, out IReadOnlyList<JiraProjectIssueType>? cachedIssueTypes))
    {
      return cachedIssueTypes;
    }

    string projectId = await GetProjectIdAsync(projectKey, cancellationToken);
    using var response = await httpClient.GetAsync(
      $"/rest/api/3/issuetype/project?projectId={Uri.EscapeDataString(projectId)}",
      cancellationToken);

    await EnsureSuccessAsync(response, $"load issue types for project {projectKey}", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

    var issueTypes = new List<JiraProjectIssueType>();
    foreach (JsonElement item in EnumerateIssueTypeElements(json.RootElement))
    {
      issueTypes.Add(new JiraProjectIssueType(
        Id: item.GetProperty("id").GetString() ?? string.Empty,
        Name: item.GetProperty("name").GetString() ?? string.Empty,
        Subtask: item.TryGetProperty("subtask", out JsonElement subtaskElement) && subtaskElement.GetBoolean()));
    }

    projectIssueTypesCache[projectKey] = issueTypes;
    return issueTypes;
  }

  public async Task<JiraRemoteIssue> GetIssueAsync(string issueKey, CancellationToken cancellationToken)
    => await GetIssueAsync(issueKey, sprintFieldId: null, cancellationToken);

  public async Task<JiraRemoteIssue> GetIssueAsync(string issueKey, string? sprintFieldId, CancellationToken cancellationToken)
  {
    string sprintFields = string.IsNullOrWhiteSpace(sprintFieldId) ? string.Empty : $",{sprintFieldId}";
    using var response = await httpClient.GetAsync(
      $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}?fields=summary,description,issuetype,status,parent,issuelinks,updated{sprintFields}",
      cancellationToken);

    await EnsureSuccessAsync(response, $"get issue {issueKey}", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
    return ParseRemoteIssue(json.RootElement, sprintFieldId);
  }

  public async Task<string> CreateIssueAsync(JiraIssuePayload payload, CancellationToken cancellationToken)
  {
    object request = await BuildIssueRequestAsync(payload, cancellationToken);
    using var response = await httpClient.PostAsync(
      "/rest/api/3/issue",
      new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
      cancellationToken);

    await EnsureSuccessAsync(response, "create issue", cancellationToken);
    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
    return json.RootElement.GetProperty("key").GetString()
      ?? throw new InvalidOperationException("Jira create issue response did not contain a key.");
  }

  public async Task UpdateIssueAsync(string issueKey, JiraIssuePayload payload, CancellationToken cancellationToken)
  {
    object request = await BuildIssueRequestAsync(payload, cancellationToken);
    using var response = await httpClient.PutAsync(
      $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}",
      new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
      cancellationToken);

    await EnsureSuccessAsync(response, $"update issue {issueKey}", cancellationToken);
  }

  public async Task EnsureIssueLinkAsync(
    string linkType,
    string outwardIssueKey,
    string inwardIssueKey,
    CancellationToken cancellationToken)
  {
    JiraRemoteIssue outwardIssue = await GetIssueAsync(outwardIssueKey, cancellationToken);
    bool alreadyExists = outwardIssue.Links.Any(link =>
      string.Equals(link.LinkType, linkType, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(link.OutwardIssueKey, outwardIssueKey, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(link.InwardIssueKey, inwardIssueKey, StringComparison.OrdinalIgnoreCase));

    if (alreadyExists)
    {
      return;
    }

    object request = new
    {
      type = new { name = linkType },
      outwardIssue = new { key = outwardIssueKey },
      inwardIssue = new { key = inwardIssueKey }
    };

    using var response = await httpClient.PostAsync(
      "/rest/api/3/issueLink",
      new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
      cancellationToken);

    await EnsureSuccessAsync(response, $"create issue link {linkType}: {outwardIssueKey} -> {inwardIssueKey}", cancellationToken);
  }

  public async Task<IReadOnlyList<JiraLinkType>> GetLinkTypesAsync(CancellationToken cancellationToken)
  {
    using var response = await httpClient.GetAsync("/rest/api/3/issueLinkType", cancellationToken);
    await EnsureSuccessAsync(response, "load Jira link types", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

    var linkTypes = new List<JiraLinkType>();
    if (json.RootElement.TryGetProperty("issueLinkTypes", out JsonElement linkTypesElement) &&
        linkTypesElement.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement item in linkTypesElement.EnumerateArray())
      {
        linkTypes.Add(new JiraLinkType(
          Id: item.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
          Name: item.GetProperty("name").GetString() ?? string.Empty,
          Inward: item.TryGetProperty("inward", out JsonElement inwardElement) ? inwardElement.GetString() ?? string.Empty : string.Empty,
          Outward: item.TryGetProperty("outward", out JsonElement outwardElement) ? outwardElement.GetString() ?? string.Empty : string.Empty));
      }
    }

    return linkTypes;
  }

  public async Task<IReadOnlyList<JiraIssueTypeStatuses>> GetProjectIssueTypeStatusesAsync(string projectKey, CancellationToken cancellationToken)
  {
    using var response = await httpClient.GetAsync(
      $"/rest/api/3/project/{Uri.EscapeDataString(projectKey)}/statuses",
      cancellationToken);

    await EnsureSuccessAsync(response, $"load statuses for project {projectKey}", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

    var issueTypeStatuses = new List<JiraIssueTypeStatuses>();
    if (json.RootElement.ValueKind != JsonValueKind.Array)
    {
      return issueTypeStatuses;
    }

    foreach (JsonElement item in json.RootElement.EnumerateArray())
    {
      string issueTypeId = item.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
      string issueTypeName = item.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;

      var statuses = new List<JiraStatus>();
      if (item.TryGetProperty("statuses", out JsonElement statusesElement) &&
          statusesElement.ValueKind == JsonValueKind.Array)
      {
        foreach (JsonElement statusItem in statusesElement.EnumerateArray())
        {
          string category = string.Empty;
          if (statusItem.TryGetProperty("statusCategory", out JsonElement categoryElement) &&
              categoryElement.TryGetProperty("name", out JsonElement categoryNameElement))
          {
            category = categoryNameElement.GetString() ?? string.Empty;
          }

          statuses.Add(new JiraStatus(
            Id: statusItem.TryGetProperty("id", out JsonElement statusIdElement) ? statusIdElement.GetString() ?? string.Empty : string.Empty,
            Name: statusItem.TryGetProperty("name", out JsonElement statusNameElement) ? statusNameElement.GetString() ?? string.Empty : string.Empty,
            Category: category));
        }
      }

      issueTypeStatuses.Add(new JiraIssueTypeStatuses(issueTypeId, issueTypeName, statuses));
    }

    return issueTypeStatuses;
  }

  public async Task<string?> GetSprintFieldIdAsync(CancellationToken cancellationToken)
  {
    using var response = await httpClient.GetAsync("/rest/api/3/field", cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return null;
    }

    await EnsureSuccessAsync(response, "load Jira fields", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

    foreach (JsonElement field in json.RootElement.EnumerateArray())
    {
      if (!field.TryGetProperty("schema", out JsonElement schemaElement) ||
          schemaElement.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      string? customSchema = schemaElement.TryGetProperty("custom", out JsonElement customElement)
        ? customElement.GetString()
        : null;

      if (!string.Equals(customSchema, "com.pyxis.greenhopper.jira:gh-sprint", StringComparison.Ordinal))
      {
        continue;
      }

      return field.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
    }

    return null;
  }

  public async Task<IReadOnlyList<JiraSprintInfo>> GetProjectSprintsAsync(string projectKey, CancellationToken cancellationToken)
  {
    List<JiraBoardInfo> boards = await GetProjectBoardsAsync(projectKey, cancellationToken);
    var sprints = new Dictionary<int, JiraSprintInfo>();

    foreach (JiraBoardInfo board in boards)
    {
      IReadOnlyList<JiraSprintInfo> boardSprints = await GetBoardSprintsAsync(board.Id, cancellationToken);
      foreach (JiraSprintInfo sprint in boardSprints)
      {
        sprints[sprint.Id] = sprint;
      }
    }

    return sprints.Values
      .OrderBy(sprint => sprint.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public async Task<IReadOnlyList<JiraRemoteIssue>> SearchProjectIssuesAsync(string projectKey, string? sprintFieldId, CancellationToken cancellationToken)
  {
    const int pageSize = 50;
    var issues = new List<JiraRemoteIssue>();
    string? nextPageToken = null;

    while (true)
    {
      object request = new
      {
        jql = $"project = {projectKey} ORDER BY created ASC",
        maxResults = pageSize,
        fields = BuildSearchFields(sprintFieldId),
        nextPageToken
      };

      using var response = await httpClient.PostAsync(
        "/rest/api/3/search/jql",
        new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
        cancellationToken);

      await EnsureSuccessAsync(response, $"search issues for project {projectKey}", cancellationToken);

      using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

      JsonElement issuesElement = json.RootElement.GetProperty("issues");
      foreach (JsonElement issueElement in issuesElement.EnumerateArray())
      {
        issues.Add(ParseRemoteIssue(issueElement, sprintFieldId));
      }

      bool isLast = json.RootElement.TryGetProperty("isLast", out JsonElement isLastElement) &&
                    isLastElement.ValueKind is JsonValueKind.True;

      nextPageToken = json.RootElement.TryGetProperty("nextPageToken", out JsonElement nextPageTokenElement) &&
                      nextPageTokenElement.ValueKind == JsonValueKind.String
        ? nextPageTokenElement.GetString()
        : null;

      if (isLast || issuesElement.GetArrayLength() == 0 || string.IsNullOrWhiteSpace(nextPageToken))
      {
        break;
      }
    }

    return issues;
  }

  public async Task<int?> TryComputeMaxIssueNumericSuffixAsync(string projectKey, CancellationToken cancellationToken)
  {
    const int sampleSize = 200;
    object request = new
    {
      jql = $"project = \"{projectKey}\" ORDER BY created DESC",
      maxResults = sampleSize,
      fields = new[] { "key" }
    };

    using var response = await httpClient.PostAsync(
      "/rest/api/3/search/jql",
      new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
      cancellationToken);

    await EnsureSuccessAsync(response, $"search recent issue keys for project {projectKey}", cancellationToken);

    using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

    if (!json.RootElement.TryGetProperty("issues", out JsonElement issuesElement) ||
        issuesElement.ValueKind != JsonValueKind.Array)
    {
      return null;
    }

    int? max = null;
    foreach (JsonElement issueElement in issuesElement.EnumerateArray())
    {
      if (!issueElement.TryGetProperty("key", out JsonElement keyElement) ||
          keyElement.ValueKind != JsonValueKind.String)
      {
        continue;
      }

      string? key = keyElement.GetString();
      if (string.IsNullOrWhiteSpace(key) ||
          !JiraIssueKeyFormat.TryParseNumericSuffix(key, projectKey, out int suffix))
      {
        continue;
      }

      max = max.HasValue ? Math.Max(max.Value, suffix) : suffix;
    }

    return max;
  }

  public async Task<IReadOnlyList<JiraRemoteIssue>> SearchProjectIssuesAsync(string projectKey, CancellationToken cancellationToken) =>
    await SearchProjectIssuesAsync(projectKey, sprintFieldId: null, cancellationToken);

  public void Dispose()
  {
    if (ownsHttpClient)
    {
      httpClient.Dispose();
    }
  }

  private async Task<string> GetProjectIdAsync(string projectKey, CancellationToken cancellationToken)
  {
    if (projectIdCache.TryGetValue(projectKey, out string? cachedProjectId))
    {
      return cachedProjectId;
    }

    JiraProjectInfo projectInfo = await GetProjectInfoAsync(projectKey, cancellationToken);
    projectIdCache[projectKey] = projectInfo.Id;
    return projectInfo.Id;
  }

  private async Task<object> BuildIssueRequestAsync(JiraIssuePayload payload, CancellationToken cancellationToken)
  {
    JiraProjectIssueType resolvedIssueType = await ResolveIssueTypeAsync(payload, cancellationToken);

    var fields = new Dictionary<string, object?>
    {
      ["project"] = new { key = payload.ProjectKey },
      ["summary"] = payload.Summary,
      ["issuetype"] = new { id = resolvedIssueType.Id },
      ["description"] = JiraAdfBuilder.BuildDocument(payload.Description)
    };

    string? sprintFieldId = await GetSprintFieldIdAsync(cancellationToken);
    if (payload.ApplySprintMapping &&
        !string.IsNullOrWhiteSpace(sprintFieldId) &&
        payload.SprintId.HasValue)
    {
      fields[sprintFieldId] = payload.SprintId.Value;
    }

    if (!string.IsNullOrWhiteSpace(payload.ParentIssueKey))
    {
      fields["parent"] = new { key = payload.ParentIssueKey };
    }

    return new { fields };
  }

  private async Task<JiraProjectIssueType> ResolveIssueTypeAsync(JiraIssuePayload payload, CancellationToken cancellationToken)
  {
    IReadOnlyList<JiraProjectIssueType> projectIssueTypes = await GetProjectIssueTypesAsync(payload.ProjectKey, cancellationToken);

    JiraProjectIssueType? resolvedIssueType = JiraMetadataResolver.ResolveIssueType(
      payload.IssueType,
      projectIssueTypes);

    if (resolvedIssueType is not null)
    {
      return resolvedIssueType;
    }

    string available = string.Join(", ", projectIssueTypes.Select(type => type.Name));
    throw new InvalidOperationException(
      $"No matching Jira issue type found in project '{payload.ProjectKey}'. " +
      $"Requested issue type: '{payload.IssueType}'. Available issue types: {available}");
  }

  private async Task<List<JiraBoardInfo>> GetProjectBoardsAsync(string projectKey, CancellationToken cancellationToken)
  {
    const int pageSize = 50;
    int startAt = 0;
    var boards = new List<JiraBoardInfo>();

    while (true)
    {
      using var response = await httpClient.GetAsync(
        $"/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKey)}&startAt={startAt}&maxResults={pageSize}",
        cancellationToken);
      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        return boards;
      }

      await EnsureSuccessAsync(response, $"load boards for project {projectKey}", cancellationToken);

      using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

      if (!json.RootElement.TryGetProperty("values", out JsonElement valuesElement) ||
          valuesElement.ValueKind != JsonValueKind.Array)
      {
        break;
      }

      foreach (JsonElement boardElement in valuesElement.EnumerateArray())
      {
        boards.Add(new JiraBoardInfo(
          boardElement.TryGetProperty("id", out JsonElement idElement) ? idElement.GetInt32() : 0,
          boardElement.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty));
      }

      bool isLast = json.RootElement.TryGetProperty("isLast", out JsonElement isLastElement) &&
                    isLastElement.ValueKind is JsonValueKind.True;
      if (isLast || valuesElement.GetArrayLength() == 0)
      {
        break;
      }

      startAt += pageSize;
    }

    return boards;
  }

  private async Task<IReadOnlyList<JiraSprintInfo>> GetBoardSprintsAsync(int boardId, CancellationToken cancellationToken)
  {
    const int pageSize = 50;
    int startAt = 0;
    var sprints = new List<JiraSprintInfo>();

    while (true)
    {
      using var response = await httpClient.GetAsync(
        $"/rest/agile/1.0/board/{boardId}/sprint?state=active,future,closed&startAt={startAt}&maxResults={pageSize}",
        cancellationToken);
      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        return sprints;
      }

      await EnsureSuccessAsync(response, $"load sprints for board {boardId}", cancellationToken);

      using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
      using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

      if (!json.RootElement.TryGetProperty("values", out JsonElement valuesElement) ||
          valuesElement.ValueKind != JsonValueKind.Array)
      {
        break;
      }

      foreach (JsonElement sprintElement in valuesElement.EnumerateArray())
      {
        sprints.Add(new JiraSprintInfo(
          sprintElement.TryGetProperty("id", out JsonElement idElement) ? idElement.GetInt32() : 0,
          sprintElement.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty,
          sprintElement.TryGetProperty("state", out JsonElement stateElement) ? stateElement.GetString() ?? string.Empty : string.Empty,
          boardId));
      }

      bool isLast = json.RootElement.TryGetProperty("isLast", out JsonElement isLastElement) &&
                    isLastElement.ValueKind is JsonValueKind.True;
      if (isLast || valuesElement.GetArrayLength() == 0)
      {
        break;
      }

      startAt += pageSize;
    }

    return sprints;
  }

  private static JiraRemoteIssue ParseRemoteIssue(JsonElement issueElement, string? sprintFieldId)
  {
    string issueKey = issueElement.GetProperty("key").GetString() ?? string.Empty;
    JsonElement fields = issueElement.GetProperty("fields");

    string? parentIssueKey = null;
    if (fields.TryGetProperty("parent", out JsonElement parentElement) &&
        parentElement.ValueKind == JsonValueKind.Object &&
        parentElement.TryGetProperty("key", out JsonElement parentKeyElement))
    {
      parentIssueKey = parentKeyElement.GetString();
    }

    string description = string.Empty;
    if (fields.TryGetProperty("description", out JsonElement descriptionElement) &&
        descriptionElement.ValueKind != JsonValueKind.Null)
    {
      description = JiraAdfBuilder.ExtractMarkdown(descriptionElement.GetRawText());
    }

    var links = new List<JiraRemoteLink>();
    if (fields.TryGetProperty("issuelinks", out JsonElement linksElement) &&
        linksElement.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement linkElement in linksElement.EnumerateArray())
      {
        string? linkType = linkElement.TryGetProperty("type", out JsonElement typeElement) &&
                           typeElement.TryGetProperty("name", out JsonElement nameElement)
          ? nameElement.GetString()
          : null;

        string? inwardIssueKey = linkElement.TryGetProperty("inwardIssue", out JsonElement inwardElement) &&
                                 inwardElement.TryGetProperty("key", out JsonElement inwardKeyElement)
          ? inwardKeyElement.GetString()
          : null;

        string? outwardIssueKey = linkElement.TryGetProperty("outwardIssue", out JsonElement outwardElement) &&
                                  outwardElement.TryGetProperty("key", out JsonElement outwardKeyElement)
          ? outwardKeyElement.GetString()
          : null;

        links.Add(new JiraRemoteLink(linkType ?? string.Empty, inwardIssueKey, outwardIssueKey));
      }
    }

    return new JiraRemoteIssue(
      IssueKey: issueKey,
      IssueType: fields.GetProperty("issuetype").GetProperty("name").GetString() ?? string.Empty,
      Status: fields.TryGetProperty("status", out JsonElement statusElement) &&
              statusElement.TryGetProperty("name", out JsonElement statusNameElement)
        ? statusNameElement.GetString() ?? string.Empty
        : string.Empty,
      Summary: fields.GetProperty("summary").GetString() ?? string.Empty,
      Description: description,
      UpdatedAt: ParseUpdatedAt(fields),
      Sprint: ParseSprint(fields, sprintFieldId),
      ParentIssueKey: parentIssueKey,
      Links: links);
  }

  private static JiraSprintInfo? ParseSprint(JsonElement fields, string? sprintFieldId)
  {
    if (string.IsNullOrWhiteSpace(sprintFieldId) ||
        !fields.TryGetProperty(sprintFieldId, out JsonElement sprintElement) ||
        sprintElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
      return null;
    }

    if (sprintElement.ValueKind == JsonValueKind.Object)
    {
      return ParseSprintObject(sprintElement);
    }

    if (sprintElement.ValueKind == JsonValueKind.Array)
    {
      JiraSprintInfo? current = sprintElement.EnumerateArray()
        .Select(ParseSprintValue)
        .FirstOrDefault(sprint => sprint is not null &&
          !string.Equals(sprint.State, "closed", StringComparison.OrdinalIgnoreCase));

      return current;
    }

    return ParseSprintValue(sprintElement);
  }

  private static JiraSprintInfo? ParseSprintValue(JsonElement element)
  {
    return element.ValueKind switch
    {
      JsonValueKind.Object => ParseSprintObject(element),
      JsonValueKind.String => ParseSprintLegacyString(element.GetString()),
      _ => null
    };
  }

  private static JiraSprintInfo? ParseSprintObject(JsonElement element)
  {
    if (!element.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out int sprintId))
    {
      return null;
    }

    int boardId = element.TryGetProperty("boardId", out JsonElement boardElement) && boardElement.TryGetInt32(out int parsedBoardId)
      ? parsedBoardId
      : 0;
    string name = element.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
    string state = element.TryGetProperty("state", out JsonElement stateElement) ? stateElement.GetString() ?? string.Empty : string.Empty;

    return new JiraSprintInfo(sprintId, name, state, boardId);
  }

  private static JiraSprintInfo? ParseSprintLegacyString(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    int id = ExtractLegacySprintInt(value, "id");
    if (id <= 0)
    {
      return null;
    }

    return new JiraSprintInfo(
      id,
      ExtractLegacySprintString(value, "name"),
      ExtractLegacySprintString(value, "state"),
      ExtractLegacySprintInt(value, "rapidViewId"));
  }

  private static DateTimeOffset ParseUpdatedAt(JsonElement fields)
  {
    if (fields.TryGetProperty("updated", out JsonElement updatedElement) &&
        updatedElement.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(updatedElement.GetString(), out DateTimeOffset updatedAt))
    {
      return updatedAt;
    }

    return DateTimeOffset.MinValue;
  }

  private static IEnumerable<JsonElement> EnumerateIssueTypeElements(JsonElement root)
  {
    if (root.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement item in root.EnumerateArray())
      {
        yield return item;
      }

      yield break;
    }

    if (root.ValueKind == JsonValueKind.Object)
    {
      if (root.TryGetProperty("issueTypes", out JsonElement issueTypesElement) &&
          issueTypesElement.ValueKind == JsonValueKind.Array)
      {
        foreach (JsonElement item in issueTypesElement.EnumerateArray())
        {
          yield return item;
        }

        yield break;
      }

      if (root.TryGetProperty("values", out JsonElement valuesElement) &&
          valuesElement.ValueKind == JsonValueKind.Array)
      {
        foreach (JsonElement item in valuesElement.EnumerateArray())
        {
          yield return item;
        }
      }
    }
  }

  private static string[] BuildSearchFields(string? sprintFieldId)
  {
    string[] baseFields = ["summary", "description", "issuetype", "status", "parent", "issuelinks", "updated"];
    return string.IsNullOrWhiteSpace(sprintFieldId)
      ? baseFields
      : [.. baseFields, sprintFieldId];
  }

  private static string ExtractLegacySprintString(string value, string key)
  {
    string marker = $"{key}=";
    int startIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (startIndex < 0)
    {
      return string.Empty;
    }

    startIndex += marker.Length;
    int endIndex = value.IndexOfAny([',', ']'], startIndex);
    if (endIndex < 0)
    {
      endIndex = value.Length;
    }

    return value[startIndex..endIndex].Trim();
  }

  private static int ExtractLegacySprintInt(string value, string key)
  {
    string parsed = ExtractLegacySprintString(value, key);
    return int.TryParse(parsed, out int number) ? number : 0;
  }

  private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
  {
    if (response.IsSuccessStatusCode)
    {
      return;
    }

    string content = await response.Content.ReadAsStringAsync(cancellationToken);
    string normalizedContent = string.IsNullOrWhiteSpace(content)
      ? "No response body."
      : content.Replace(System.Environment.NewLine, " ").Trim();
    throw new InvalidOperationException(
      $"Jira API failed to {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Details: {normalizedContent}");
  }

  private static HttpClient CreateConfiguredHttpClient(JiraSettings settings)
  {
    var httpClient = new HttpClient
    {
      BaseAddress = settings.BaseUri
    };

    string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Email}:{settings.ApiToken}"));
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return httpClient;
  }
}

public sealed record JiraProjectInfo(
  string Id,
  string Key,
  string Name);

internal sealed record JiraBoardInfo(
  int Id,
  string Name);
