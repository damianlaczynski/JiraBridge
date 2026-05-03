using System.Globalization;

namespace JiraBridge.Infrastructure.Jira;

public static class JiraIssueKeyFormat
{
  public static bool TryParseNumericSuffix(string issueKey, string projectKey, out int suffix)
  {
    suffix = 0;
    if (string.IsNullOrWhiteSpace(issueKey) || string.IsNullOrWhiteSpace(projectKey))
    {
      return false;
    }

    string trimmedKey = issueKey.Trim();
    string prefix = $"{projectKey.Trim()}-";
    if (!trimmedKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    string tail = trimmedKey[prefix.Length..];
    return int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out suffix);
  }

  public static int? MergeNullableMax(int? a, int? b)
  {
    if (!a.HasValue)
    {
      return b;
    }

    if (!b.HasValue)
    {
      return a;
    }

    return Math.Max(a.Value, b.Value);
  }
}
