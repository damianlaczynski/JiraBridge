using JiraBridge.Infrastructure.Parsing;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class ArtifactMarkdownParserTests
{
  [Fact]
  public void TryParse_ReadsKnownSectionsAndValues()
  {
    string path = Path.GetTempFileName();

    try
    {
      File.WriteAllText(
        path,
        """
        # Sample title

        ## Description

        Example text.

        ## Links

        - Parent: none

        ## Relations

        ### Blocks

        - ../other.md

        ## Metadata

        - Issue Type: Story
        """);

      var document = ArtifactMarkdownParser.TryParse(path, out List<string> errors);

      Assert.NotNull(document);
      Assert.Empty(errors);
      Assert.Equal("Sample title", document!.Title);
      Assert.Equal("Story", document.JiraIssueType);
      Assert.Single(document.GetNestedList("Relations", "Blocks"));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public void TryParse_NormalizesMarkdownFileLinksToPlainPaths()
  {
    string path = Path.GetTempFileName();

    try
    {
      File.WriteAllText(
        path,
        """
        # Sample title

        ## Description

        Example text.

        ## Links

        - Parent: [../parent.md](../parent.md)

        ## Relations

        ### Blocks

        - [../other.md](../other.md)

        ## Metadata

        - Issue Type: Story
        """);

      var document = ArtifactMarkdownParser.TryParse(path, out List<string> errors);

      Assert.NotNull(document);
      Assert.Empty(errors);
      Assert.Equal("../parent.md", document!.Parent);
      Assert.Equal("../other.md", Assert.Single(document.GetNestedList("Relations", "Blocks")));
    }
    finally
    {
      File.Delete(path);
    }
  }
}
