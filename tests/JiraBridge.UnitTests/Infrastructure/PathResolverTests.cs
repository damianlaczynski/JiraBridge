using JiraBridge.Infrastructure.Repository;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class PathResolverTests
{
  [Fact]
  public void ResolveRepoRelativePath_AllowsPathInsideRepository()
  {
    string repoRoot = Path.GetFullPath(Path.Combine("C:", "repo"));

    string resolved = PathResolver.ResolveRepoRelativePath(repoRoot, ".jirabridge/settings.json");

    Assert.EndsWith(Path.Combine(".jirabridge", "settings.json"), resolved, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ResolveRepoRelativePath_RejectsPathTraversalOutsideRepository()
  {
    string repoRoot = Path.GetFullPath(Path.Combine("C:", "repo"));

    Assert.Throws<InvalidOperationException>(() =>
      PathResolver.ResolveRepoRelativePath(repoRoot, "..\\outside.json"));
  }

  [Fact]
  public void AreRepositoryRelativePathsEqual_IgnoresSlashDirectionAndLeadingDots()
  {
    Assert.True(PathResolver.AreRepositoryRelativePathsEqual(
      @"docs\jira-bridge\a.md",
      "docs/jira-bridge/a.md"));

    Assert.True(PathResolver.AreRepositoryRelativePathsEqual(
      "./docs/jira-bridge/a.md",
      @"docs\jira-bridge\a.md"));
  }

  [Fact]
  public void AreRepositoryRelativePathsEqual_ReturnsFalseWhenSegmentsDiffer()
  {
    Assert.False(PathResolver.AreRepositoryRelativePathsEqual(
      "docs/jira-bridge/a.md",
      "docs/jira-bridge/b.md"));
  }
}
