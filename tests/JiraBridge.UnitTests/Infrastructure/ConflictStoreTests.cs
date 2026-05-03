using JiraBridge.Domain.Sync;
using JiraBridge.Infrastructure.Storage;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class ConflictStoreTests
{
  [Fact]
  public async Task GetOpenConflictsAsync_ReadsPersistedConflictRecords()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));

    using var scope = new TestProcessScope(repoRoot);

    ConflictStore.Record(
      repoRoot,
      new ConflictRecord("SCRUM-1", "docs/jira-bridge/story/item.md", "push", "Title", "Story", "L", "R", "details"));

    var store = new ConflictStore();
    IReadOnlyCollection<SyncConflict> conflicts = await store.GetOpenConflictsAsync(CancellationToken.None);

    SyncConflict conflict = Assert.Single(conflicts);
    Assert.Equal("SCRUM-1", conflict.IssueKey);
    Assert.Equal("Title", conflict.Title);
    Assert.Equal("Story", conflict.IssueType);
    Assert.Equal("push", conflict.Operation);
    Assert.Contains("push", conflict.Summary);
    Assert.Contains("Title", conflict.Summary);
    Assert.Equal("details", conflict.Details);
  }

  [Fact]
  public async Task Clear_RemovesConflictFileEntry()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));

    using var scope = new TestProcessScope(repoRoot);

    ConflictStore.Record(
      repoRoot,
      new ConflictRecord("SCRUM-2", "docs/jira-bridge/story/item.md", "pull", "Title", "Story", "L", "R", "details"));

    ConflictStore.Clear(repoRoot, "SCRUM-2");

    var store = new ConflictStore();
    IReadOnlyCollection<SyncConflict> conflicts = await store.GetOpenConflictsAsync(CancellationToken.None);

    Assert.Empty(conflicts);
  }

  [Fact]
  public async Task GetOpenConflictsAsync_WhenStoreFileIsInvalid_ThrowsWithPathContext()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
    Directory.CreateDirectory(Path.Combine(repoRoot, ".jirabridge"));
    File.WriteAllText(Path.Combine(repoRoot, ".jirabridge", "conflicts.json"), "{ invalid json");

    using var scope = new TestProcessScope(repoRoot);

    var store = new ConflictStore();

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      store.GetOpenConflictsAsync(CancellationToken.None));

    Assert.Contains("conflicts.json", ex.Message);
    Assert.Contains("invalid JSON", ex.Message);
  }

  [Fact]
  public void Clear_WhenConflictDoesNotExist_DoesNotCreateStoreFile()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));

    using var scope = new TestProcessScope(repoRoot);

    ConflictStore.Clear(repoRoot, "SCRUM-404");

    Assert.False(File.Exists(Path.Combine(repoRoot, ".jirabridge", "conflicts.json")));
  }
}
