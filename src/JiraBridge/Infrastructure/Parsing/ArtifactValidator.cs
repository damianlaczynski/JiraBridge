using System.Text.RegularExpressions;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Parsing;

public static partial class ArtifactValidator
{
  private static readonly string[] RequiredSections =
  [
    "Metadata",
    "Links",
    "Relations",
    "Description"
  ];

  public static IReadOnlyList<ValidationIssue> Validate(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    RepositoryJiraConfiguration? jiraConfiguration,
    string repoRoot)
  {
    var issues = new List<ValidationIssue>();

    foreach (string sectionName in RequiredSections)
    {
      if (!document.Sections.ContainsKey(sectionName))
      {
        issues.Add(new ValidationIssue(document.Path, $"Missing required section '## {sectionName}'."));
      }
    }

    if (issues.Count > 0)
    {
      return issues;
    }

    ValidateMetadata(document, jiraConfiguration, issues);
    ValidateLinks(document, documents, jiraConfiguration, issues);
    ValidateRelations(document, documents, jiraConfiguration, repoRoot, issues);
    ValidateDescription(document, issues);

    return issues;
  }

  private static void ValidateMetadata(
    ArtifactDocument document,
    RepositoryJiraConfiguration? jiraConfiguration,
    ICollection<ValidationIssue> issues)
  {
    string? issueType = document.JiraIssueType;
    if (string.IsNullOrWhiteSpace(issueType))
    {
      issues.Add(new ValidationIssue(document.Path, "Metadata is missing 'Issue Type'."));
    }
    else if (jiraConfiguration is not null &&
             JiraMetadataResolver.ResolveIssueType(issueType, jiraConfiguration.IssueTypes) is null)
    {
      issues.Add(new ValidationIssue(
        document.Path,
        $"Issue Type '{issueType}' does not exist in Jira project '{jiraConfiguration.ProjectKey}'."));
    }

    string? issueKey = document.JiraIssueKey;
    if (!string.IsNullOrWhiteSpace(issueKey) && !JiraIssueKeyRegex().IsMatch(issueKey))
    {
      issues.Add(new ValidationIssue(document.Path, $"Invalid Jira Issue Key '{issueKey}'."));
    }
  }

  private static void ValidateLinks(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    RepositoryJiraConfiguration? jiraConfiguration,
    ICollection<ValidationIssue> issues)
  {
    string? issueTypeName = document.JiraIssueType;
    string? parent = document.Parent;

    if (parent is null)
    {
      issues.Add(new ValidationIssue(document.Path, "Links section is missing 'Parent'."));
      return;
    }

    JiraProjectIssueType? issueType = !string.IsNullOrWhiteSpace(issueTypeName) && jiraConfiguration is not null
      ? JiraMetadataResolver.ResolveIssueType(issueTypeName, jiraConfiguration.IssueTypes)
      : null;

    if (PathResolver.IsNone(parent))
    {
      if (issueType?.Subtask == true)
      {
        issues.Add(new ValidationIssue(document.Path, $"Issue Type '{issueTypeName}' is a sub-task and requires a parent link."));
      }

      return;
    }

    string resolvedPath = PathResolver.ResolveArtifactRelativePath(document.Path, parent);
    if (!documents.TryGetValue(resolvedPath, out ArtifactDocument? parentDocument))
    {
      issues.Add(new ValidationIssue(document.Path, $"Parent path does not resolve to an artifact: {parent}"));
      return;
    }

    if (issueType?.Subtask == true)
    {
      JiraProjectIssueType? parentIssueType = !string.IsNullOrWhiteSpace(parentDocument.JiraIssueType) && jiraConfiguration is not null
        ? JiraMetadataResolver.ResolveIssueType(parentDocument.JiraIssueType, jiraConfiguration.IssueTypes)
        : null;

      if (parentIssueType?.Subtask == true)
      {
        issues.Add(new ValidationIssue(
          document.Path,
          $"Issue Type '{issueTypeName}' cannot use another sub-task as parent: {parent}."));
      }
    }
  }

  private static void ValidateRelations(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    RepositoryJiraConfiguration? jiraConfiguration,
    string repoRoot,
    ICollection<ValidationIssue> issues)
  {
    SectionContent section = document.Sections["Relations"];
    foreach ((string nestedSection, List<string> items) in section.NestedLists)
    {
      if (jiraConfiguration is not null &&
          JiraMetadataResolver.ResolveLinkType(nestedSection, jiraConfiguration.LinkTypes) is null)
      {
        issues.Add(new ValidationIssue(
          document.Path,
          $"Relation subsection '{nestedSection}' does not match any Jira link type available in project '{jiraConfiguration.ProjectKey}'."));
      }

      foreach (string item in items)
      {
        if (PathResolver.IsNone(item))
        {
          continue;
        }

        string resolvedPath = PathResolver.ResolveArtifactRelativePath(document.Path, item);
        bool exists = documents.ContainsKey(resolvedPath) || File.Exists(resolvedPath);
        if (!exists)
        {
          issues.Add(new ValidationIssue(document.Path, $"Relation path does not exist in '{nestedSection}': {item}"));
        }
        else if (!resolvedPath.StartsWith(Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase))
        {
          issues.Add(new ValidationIssue(document.Path, $"Relation path resolves outside repository root: {item}"));
        }
      }
    }
  }

  private static void ValidateDescription(ArtifactDocument document, ICollection<ValidationIssue> issues)
  {
    SectionContent section = document.Sections["Description"];
    string description = string.Join(System.Environment.NewLine, section.BodyLines).Trim();
    if (string.IsNullOrWhiteSpace(description))
    {
      issues.Add(new ValidationIssue(document.Path, "Description section must not be empty."));
    }
  }

  [GeneratedRegex(@"^[A-Z][A-Z0-9]+-\d+$", RegexOptions.CultureInvariant)]
  private static partial Regex JiraIssueKeyRegex();
}
