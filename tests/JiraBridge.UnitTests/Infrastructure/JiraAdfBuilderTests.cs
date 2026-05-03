using System.Text.Json;
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
  public void ExtractMarkdown_RendersTaskListCheckboxes()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"taskList",
            "content":[
              {
                "type":"taskItem",
                "attrs":{"state":"TODO"},
                "content":[{"type":"paragraph","content":[{"type":"text","text":"Todo item"}]}]
              },
              {
                "type":"taskItem",
                "attrs":{"state":"DONE"},
                "content":[{"type":"paragraph","content":[{"type":"text","text":"Done item"}]}]
              }
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("- [ ] Todo item", markdown);
    Assert.Contains("- [x] Done item", markdown);
  }

  [Fact]
  public void ExtractMarkdown_TaskItem_WithDirectTextNodes_RendersLabels()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"taskList",
            "content":[
              {
                "type":"taskItem",
                "attrs":{"state":"TODO"},
                "content":[{"type":"text","text":"asdasd"}]
              },
              {
                "type":"taskItem",
                "attrs":{"state":"DONE"},
                "content":[{"type":"text","text":"sd"}]
              }
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("- [ ] asdasd", markdown);
    Assert.Contains("- [x] sd", markdown);
  }

  [Fact]
  public void BuildDocument_TaskList_RoundTripsThroughExtractMarkdown()
  {
    const string markdown =
      """
      - [ ] Open
      - [x] Closed
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);

    string roundTrip = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("- [ ] Open", roundTrip);
    Assert.Contains("- [x] Closed", roundTrip);
  }

  [Fact]
  public void BuildDocument_TaskList_SerializedAdf_IncludesLocalIdAttrs()
  {
    const string markdown =
      """
      - [ ] Open
      - [x] Closed
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);

    Assert.Contains("\"type\":\"taskList\"", adfJson);
    Assert.Contains("\"localId\":", adfJson);
    Assert.Contains("\"type\":\"taskItem\"", adfJson);
    Assert.Contains("\"state\":\"TODO\"", adfJson);
    Assert.Contains("\"state\":\"DONE\"", adfJson);
  }

  [Fact]
  public void BuildDocument_CheckboxMarkdown_TaskItems_UseInlineContentForApiCompliance()
  {
    const string markdown =
      """
      - [ ] Open
      - [x] Closed
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);

    using var doc = System.Text.Json.JsonDocument.Parse(adfJson);
    JsonElement root = doc.RootElement;
    JsonElement taskList = root.GetProperty("content")[0];
    Assert.Equal("taskList", taskList.GetProperty("type").GetString());
    JsonElement firstItem = taskList.GetProperty("content")[0];
    Assert.Equal("taskItem", firstItem.GetProperty("type").GetString());
    JsonElement firstChild = firstItem.GetProperty("content")[0];
    Assert.Equal("text", firstChild.GetProperty("type").GetString());

    string roundTrip = JiraAdfBuilder.ExtractMarkdown(adfJson);
    Assert.Contains("- [ ] Open", roundTrip);
    Assert.Contains("- [x] Closed", roundTrip);
  }

  [Fact]
  public void BuildDocument_ForJiraRest_CheckboxMarkdown_CanStillEmitBulletsWhenOptOutNativeTasks()
  {
    const string markdown =
      """
      - [ ] Open
      - [x] Closed
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown, useNativeTaskLists: false);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);

    Assert.Contains("\"type\":\"bulletList\"", adfJson);
    Assert.DoesNotContain("taskList", adfJson);
    Assert.Contains("- [ ] Open", JiraAdfBuilder.ExtractMarkdown(adfJson));
    Assert.Contains("- [x] Closed", JiraAdfBuilder.ExtractMarkdown(adfJson));
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
  public void BuildDocument_RoundTripsRichInlineHorizontalRuleAndOrderedList()
  {
    const string markdown =
      """
      Mix *italic*, ~~strike~~, `code`, [link](https://example.com), **bold**.

      ---

      3. third
      4. fourth
      """;

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);
    string roundTrip = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("*italic*", roundTrip);
    Assert.Contains("~~strike~~", roundTrip);
    Assert.Contains("`code`", roundTrip);
    Assert.Contains("[link](https://example.com)", roundTrip);
    Assert.Contains("**bold**", roundTrip);
    Assert.Contains("---", roundTrip);
    Assert.Contains("3. third", roundTrip);
    Assert.Contains("4. fourth", roundTrip);
  }

  [Fact]
  public void BuildDocument_LinkLabelAllowsNestedMarkdown()
  {
    const string markdown = """[**bold label**](https://example.org/path)""";

    object adf = JiraAdfBuilder.BuildDocument(markdown);
    string adfJson = System.Text.Json.JsonSerializer.Serialize(adf);
    string roundTrip = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("[**bold label**](https://example.org/path)", roundTrip);
  }

  [Fact]
  public void ExtractMarkdown_RendersOrderedListWithStartingOrderFromAttrs()
  {
    const string adfJson =
      """
      {
        "type":"doc",
        "version":1,
        "content":[
          {
            "type":"orderedList",
            "attrs":{"order":5},
            "content":[
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Five"}]}]},
              {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Six"}]}]}
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("5. Five", markdown);
    Assert.Contains("6. Six", markdown);
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

  [Fact]
  public void ExtractMarkdown_RendersDecisionListAsCheckboxLines()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"decisionList",
            "attrs":{"localId":"list-1"},
            "content":[
              {"type":"decisionItem","attrs":{"localId":"i1","state":"DECIDED"},"content":[{"type":"text","text":"Yes"}]},
              {"type":"decisionItem","attrs":{"localId":"i2","state":"OPEN"},"content":[{"type":"text","text":"No"}]}
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("- [x] Yes", markdown);
    Assert.Contains("- [ ] No", markdown);
  }

  [Fact]
  public void ExtractMarkdown_RendersExpandTitleAndBody()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"expand",
            "attrs":{"title":"More"},
            "content":[{"type":"paragraph","content":[{"type":"text","text":"Hidden"}]}]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("Expand: More", markdown);
    Assert.Contains("Hidden", markdown);
  }

  [Fact]
  public void ExtractMarkdown_RendersExternalMediaAsMarkdownLink()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"mediaSingle",
            "attrs":{"layout":"center"},
            "content":[{"type":"media","attrs":{"type":"external","url":"https://example.com/a.png","alt":"Shot"}}]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("[Shot](https://example.com/a.png)", markdown);
  }

  [Fact]
  public void ExtractMarkdown_RendersUnderlineSubsupAndColorsAsHtmlSpans()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"paragraph",
            "content":[
              {"type":"text","text":"u","marks":[{"type":"underline"}]},
              {"type":"text","text":"s","marks":[{"type":"subsup","attrs":{"type":"sub"}}]},
              {"type":"text","text":"p","marks":[{"type":"subsup","attrs":{"type":"sup"}}]},
              {"type":"text","text":"c","marks":[{"type":"textColor","attrs":{"color":"#ff0000"}}]},
              {"type":"text","text":"b","marks":[{"type":"backgroundColor","attrs":{"color":"#00ff00"}}]}
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("<u>u</u>", markdown);
    Assert.Contains("<sub>s</sub>", markdown);
    Assert.Contains("<sup>p</sup>", markdown);
    Assert.Contains("<span style=\"color:#ff0000\">c</span>", markdown);
    Assert.Contains("<span style=\"background-color:#00ff00\">b</span>", markdown);
  }

  [Fact]
  public void ExtractMarkdown_RendersLayoutSectionColumnsInOrder()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {
            "type":"layoutSection",
            "content":[
              {"type":"layoutColumn","attrs":{"width":50},"content":[{"type":"paragraph","content":[{"type":"text","text":"Left"}]}]},
              {"type":"layoutColumn","attrs":{"width":50},"content":[{"type":"paragraph","content":[{"type":"text","text":"Right"}]}]}
            ]
          }
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    int left = markdown.IndexOf("Left", StringComparison.Ordinal);
    int right = markdown.IndexOf("Right", StringComparison.Ordinal);
    Assert.True(left >= 0 && right > left);
  }

  [Fact]
  public void ExtractMarkdown_RendersExtensionEmbedAndDateNodes()
  {
    const string adfJson =
      """
      {
        "type":"doc","version":1,
        "content":[
          {"type":"embedCard","attrs":{"url":"https://embed.example/x","layout":"center"}},
          {"type":"paragraph","content":[
            {"type":"date","attrs":{"timestamp":"2024-06-15T12:00:00Z"}},
            {"type":"status","attrs":{"text":"In Progress","color":"blue"}},
            {"type":"placeholder","attrs":{"text":"TODO"}},
            {"type":"inlineExtension","attrs":{"extensionKey":"loom","extensionType":"com.loom","text":"Video"}}
          ]}
        ]
      }
      """;

    string markdown = JiraAdfBuilder.ExtractMarkdown(adfJson);

    Assert.Contains("<https://embed.example/x>", markdown);
    Assert.Contains("2024-06-15", markdown);
    Assert.Contains("[In Progress]", markdown);
    Assert.Contains("{TODO}", markdown);
    Assert.Contains("Video", markdown);
  }
}
