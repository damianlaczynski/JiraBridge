using JiraBridge.Infrastructure.Jira;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class JiraAdfBuilderTests
{
  [Fact]
  public void ExtractMarkdown_RendersHeadingListAndTable()
  {
    const string adfJson =
      """
      {
        "type":"doc",
        "version":1,
        "content":[
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Section"}]},
          {"type":"paragraph","content":[{"type":"text","text":"Paragraph"}]},
          {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Item"}]}]}]},
          {"type":"table","content":[
            {"type":"tableRow","content":[
              {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]},
              {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]}
            ]},
            {"type":"tableRow","content":[
              {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]},
              {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"2"}]}]}
            ]}
          ]}
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("## Section", markdown);
    Assert.Contains("Paragraph", markdown);
    Assert.Contains("- Item", markdown);
    Assert.Contains("| A | B |", markdown);
  }

  [Fact]
  public void ExtractPlainText_ReturnsEmptyForInvalidJson()
  {
    string result = JiraAdfBuilder.ExtractPlainText("not-json");

    Assert.Equal(string.Empty, result);
  }

  [Fact]
  public void BuildDocument_ExtractMarkdown_RoundTripsCommonFormatting()
  {
    const string markdown =
      """
      ## Scope

      Support **bold** text
      across lines.

      - First
      - Second

      | Name | Value |
      | --- | --- |
      | A | 1 |
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);

    string roundTrip = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("## Scope", roundTrip);
    Assert.Contains("Support **bold** text", roundTrip);
    Assert.Contains("across lines.", roundTrip);
    Assert.Contains("- First", roundTrip);
    Assert.Contains("- Second", roundTrip);
    Assert.Contains("| Name | Value |", roundTrip);
    Assert.Contains("| A | 1 |", roundTrip);
  }

  [Fact]
  public void ExtractMarkdown_RendersOrderedListCodeBlockAndBlockQuote()
  {
    const string adfJson =
      """
      {
        "type":"doc",
        "version":1,
        "content":[
          {
            "type":"orderedList",
            "content":[
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"One"}]}]},
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Two"}]}]}
            ]
          },
          {
            "type":"codeBlock",
            "attrs":{"language":"json"},
            "content":[{"type":"text","text":"{\"ok\":true}"}]
          },
          {
            "type":"blockquote",
            "content":[{"type":"paragraph","content":[{"type":"text","text":"Quoted"}]}]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("1. One", markdown);
    Assert.Contains("2. Two", markdown);
    Assert.Contains("```json", markdown);
    Assert.Contains("{\"ok\":true}", markdown);
    Assert.Contains("> Quoted", markdown);
  }
}
