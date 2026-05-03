using JiraBridge.Infrastructure.Jira;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class JiraIssueKeyFormatTests
{
  [Theory]
  [InlineData("SCRUM-21", "SCRUM", 21)]
  [InlineData("scrum-5", "SCRUM", 5)]
  [InlineData("OPS-100", "OPS", 100)]
  public void TryParseNumericSuffix_ValidKeys_ReturnsNumber(string issueKey, string projectKey, int expected)
  {
    Assert.True(JiraIssueKeyFormat.TryParseNumericSuffix(issueKey, projectKey, out int n));
    Assert.Equal(expected, n);
  }

  [Theory]
  [InlineData("FOO-1", "SCRUM")]
  [InlineData("SCRUM-", "SCRUM")]
  [InlineData("SCRUM-x", "SCRUM")]
  public void TryParseNumericSuffix_Invalid_ReturnsFalse(string issueKey, string projectKey)
  {
    Assert.False(JiraIssueKeyFormat.TryParseNumericSuffix(issueKey, projectKey, out _));
  }

  [Fact]
  public void MergeNullableMax_ReturnsMaximumOrEitherSide()
  {
    Assert.Null(JiraIssueKeyFormat.MergeNullableMax(null, null));
    Assert.Equal(3, JiraIssueKeyFormat.MergeNullableMax(3, null));
    Assert.Equal(7, JiraIssueKeyFormat.MergeNullableMax(null, 7));
    Assert.Equal(10, JiraIssueKeyFormat.MergeNullableMax(3, 10));
  }
}
