using JiraBridge.Infrastructure.Storage;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class ArtifactFileUpdaterTests
{
  [Fact]
  public void WriteSyncMetadata_UpdatesExistingKeysAndRemovesObsoleteOnes()
  {
    string path = Path.GetTempFileName();

    try
    {
      File.WriteAllText(
        path,
        """
        # Item

        ## Description

        Text

        ## Metadata

        - Issue Type: Story
        - Jira Issue Key: SCRUM-1
        - Jira Last Synced Repo Hash: old
        """);

      ArtifactFileUpdater.WriteSyncMetadata(path, "SCRUM-2", "LOCAL", "REMOTE");
      string content = File.ReadAllText(path);

      Assert.Contains("- Jira Issue Key: SCRUM-2", content);
      Assert.Contains("- Jira Last Synced Local Hash: LOCAL", content);
      Assert.Contains("- Jira Last Synced Remote Hash: REMOTE", content);
      Assert.DoesNotContain("Jira Last Synced Repo Hash", content);
      Assert.True(content.IndexOf("## Description", StringComparison.Ordinal) < content.IndexOf("## Metadata", StringComparison.Ordinal));
    }
    finally
    {
      File.Delete(path);
    }
  }
}
