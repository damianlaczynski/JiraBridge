using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Storage;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class PlanBuilderTests
{
  [Fact]
  public void OrderDocuments_PutsParentBeforeChild()
  {
    var parent = new ArtifactDocument
    {
      Path = Path.GetFullPath("parent.md"),
      Title = "Parent",
      Sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase)
      {
        ["Links"] = new(),
        ["Relations"] = new()
      }
    };

    var child = new ArtifactDocument
    {
      Path = Path.GetFullPath("child.md"),
      Title = "Child",
      Sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase)
      {
        ["Links"] = new() { KeyValues = { ["Parent"] = "parent.md" } },
        ["Relations"] = new()
      }
    };

    List<ArtifactDocument> ordered = PlanBuilder.OrderDocuments([child, parent]);

    Assert.Equal(parent.Path, ordered[0].Path);
    Assert.Equal(child.Path, ordered[1].Path);
  }
}
