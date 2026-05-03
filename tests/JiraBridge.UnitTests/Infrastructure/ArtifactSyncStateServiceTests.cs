using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Storage;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class ArtifactSyncStateServiceTests
{
  [Fact]
  public void ComputeLocalFingerprint_ChangesWhenDescriptionChanges()
  {
    ArtifactDocument first = CreateDocument("One");
    ArtifactDocument second = CreateDocument("Two");

    string firstHash = ArtifactSyncStateService.ComputeLocalFingerprint(first);
    string secondHash = ArtifactSyncStateService.ComputeLocalFingerprint(second);

    Assert.NotEqual(firstHash, secondHash);
  }

  [Fact]
  public void HasRemoteChanges_ReturnsFalseForMatchingFingerprint()
  {
    var remoteIssue = new JiraRemoteIssue(
      "SCRUM-1",
      "Story",
      "To Do",
      "Title",
      "Description",
      DateTimeOffset.UtcNow,
      null,
      []);

    string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
    ArtifactDocument document = CreateDocument("Description", issueKey: "SCRUM-1", remoteHash: remoteHash);

    Assert.False(ArtifactSyncStateService.HasRemoteChanges(document, remoteIssue));
  }

  private static ArtifactDocument CreateDocument(string description, string issueKey = "", string remoteHash = "")
  {
    var descriptionSection = new SectionContent();
    descriptionSection.BodyLines.Add(description);

    var metadata = new SectionContent();
    metadata.KeyValues["Issue Type"] = "Story";
    metadata.KeyValues["Status"] = "To Do";
    metadata.KeyValues["Jira Issue Key"] = issueKey;
    metadata.KeyValues["Jira Last Synced Remote Hash"] = remoteHash;

    return new ArtifactDocument
    {
      Path = Path.GetFullPath(Guid.NewGuid().ToString("N") + ".md"),
      Title = "Title",
      Sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase)
      {
        ["Metadata"] = metadata,
        ["Links"] = new(),
        ["Relations"] = new(),
        ["Description"] = descriptionSection
      }
    };
  }
}
