using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class ChatMarkdownFormatterTests
{
    #region Tables

    [Fact]
    public void ToHtml_RendersThePlanningTable_InsteadOfRawPipesAndDashes()
    {
        // The reported case: 20 available hours against tasks estimated at 12, 10 and 6 hours.
        var content =
            "Here's how your week looks:\n" +
            "\n" +
            "| Task | Estimate (hours) |\n" +
            "|------|-----------------:|\n" +
            "| Task A | 12 |\n" +
            "| Task B | 10 |\n" +
            "| Task C | 6 |\n" +
            "| **Total** | **28** |\n" +
            "\n" +
            "That's 8 hours over your 20 available.";

        var html = ChatMarkdownFormatter.ToHtml(content);

        Assert.Equal(
            "Here&#39;s how your week looks:" +
            "<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\">" +
            "<thead><tr><th>Task</th><th style=\"text-align:right\">Estimate (hours)</th></tr></thead>" +
            "<tbody>" +
            "<tr><td>Task A</td><td style=\"text-align:right\">12</td></tr>" +
            "<tr><td>Task B</td><td style=\"text-align:right\">10</td></tr>" +
            "<tr><td>Task C</td><td style=\"text-align:right\">6</td></tr>" +
            "<tr><td><strong>Total</strong></td><td style=\"text-align:right\"><strong>28</strong></td></tr>" +
            "</tbody></table></div>" +
            "That&#39;s 8 hours over your 20 available.",
            html);
        Assert.DoesNotContain("|", html);
        Assert.DoesNotContain("---", html);
    }

    [Fact]
    public void ToHtml_RendersATableWithoutOuterPipes()
    {
        var html = ChatMarkdownFormatter.ToHtml("Day | Hours\n--- | ---\nMon | 4\nTue | 6");

        Assert.Equal(
            "<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\">" +
            "<thead><tr><th>Day</th><th>Hours</th></tr></thead>" +
            "<tbody><tr><td>Mon</td><td>4</td></tr><tr><td>Tue</td><td>6</td></tr></tbody>" +
            "</table></div>",
            html);
    }

    [Fact]
    public void ToHtml_AppliesEveryColumnAlignment()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a | b | c | d |\n| --- | :-- | :-: | --: |\n| 1 | 2 | 3 | 4 |");

        Assert.Contains("<th>a</th><th style=\"text-align:left\">b</th><th style=\"text-align:center\">c</th><th style=\"text-align:right\">d</th>", html);
        Assert.Contains("<td>1</td><td style=\"text-align:left\">2</td><td style=\"text-align:center\">3</td><td style=\"text-align:right\">4</td>", html);
    }

    [Fact]
    public void ToHtml_PadsShortRowsAndDropsExtraCells()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a | b |\n|---|---|\n| only |\n| 1 | 2 | 3 |");

        Assert.Contains("<tr><td>only</td><td></td></tr>", html);
        Assert.Contains("<tr><td>1</td><td>2</td></tr>", html);
        Assert.DoesNotContain(">3<", html);
    }

    [Fact]
    public void ToHtml_TreatsEscapedPipeAsCellText()
    {
        var html = ChatMarkdownFormatter.ToHtml("| Option | Meaning |\n|---|---|\n| a \\| b | either |");

        Assert.Contains("<td>a | b</td><td>either</td>", html);
    }

    [Fact]
    public void ToHtml_HandlesWindowsLineEndings()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a | b |\r\n|---|---|\r\n| 1 | 2 |\r\nafter");

        Assert.Contains("<tbody><tr><td>1</td><td>2</td></tr></tbody></table></div>after", html);
        Assert.DoesNotContain("\r", html);
    }

    [Fact]
    public void ToHtml_RendersMultipleTablesWithTextBetween()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a |\n|---|\n| 1 |\n\nmiddle\n\n| b |\n|---|\n| 2 |");

        Assert.Equal(
            "<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\"><thead><tr><th>a</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table></div>" +
            "middle" +
            "<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\"><thead><tr><th>b</th></tr></thead><tbody><tr><td>2</td></tr></tbody></table></div>",
            html);
    }

    [Fact]
    public void ToHtml_EndsTheTableAtTheFirstLineWithoutAPipe()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |\nNext steps follow.");

        Assert.EndsWith("</table></div>Next steps follow.", html);
    }

    [Fact]
    public void ToHtml_RendersAHeaderOnlyTable_WhileTheBodyIsStillStreaming()
    {
        var html = ChatMarkdownFormatter.ToHtml("| Task | Hours |\n|---|---|");

        Assert.Equal(
            "<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\"><thead><tr><th>Task</th><th>Hours</th></tr></thead><tbody></tbody></table></div>",
            html);
    }

    #endregion

    #region Not tables

    [Fact]
    public void ToHtml_LeavesAPipeRowWithoutADelimiterRowAsText()
    {
        // Mid-stream, before the delimiter row has arrived.
        Assert.Equal("| Task | Hours |", ChatMarkdownFormatter.ToHtml("| Task | Hours |"));
        Assert.Equal("a | b<br>c | d", ChatMarkdownFormatter.ToHtml("a | b\nc | d"));
    }

    [Fact]
    public void ToHtml_RequiresTheDelimiterColumnCountToMatchTheHeader()
    {
        var html = ChatMarkdownFormatter.ToHtml("| a | b |\n|---|\n| 1 | 2 |");

        Assert.DoesNotContain("<table", html);
        Assert.Equal("| a | b |<br>|---|<br>| 1 | 2 |", html);
    }

    [Fact]
    public void ToHtml_DoesNotTreatASetextUnderlineOrRuleAsADelimiterRow()
    {
        Assert.DoesNotContain("<table", ChatMarkdownFormatter.ToHtml("a | b\n---"));
        Assert.DoesNotContain("<table", ChatMarkdownFormatter.ToHtml("| a | b |\n| x | y |"));
        Assert.DoesNotContain("<table", ChatMarkdownFormatter.ToHtml("| a | b |\n|:|::|"));
    }

    [Fact]
    public void ToHtml_NeverBuildsATableInsideAFencedCodeBlock()
    {
        var html = ChatMarkdownFormatter.ToHtml("```\n| a | b |\n|---|---|\n| 1 | 2 |\n```");

        Assert.DoesNotContain("<table", html);
        Assert.Contains("| a | b |<br>|---|---|<br>| 1 | 2 |", html);
    }

    [Fact]
    public void ToHtml_BuildsATableAfterAClosedFence()
    {
        var html = ChatMarkdownFormatter.ToHtml("```\ncode\n```\n| a |\n|---|\n| 1 |");

        Assert.Contains("<table class=\"ai-chat-table\">", html);
    }

    #endregion

    #region Encoding and existing inline rules

    [Fact]
    public void ToHtml_EncodesHtmlInTextAndInCells()
    {
        var html = ChatMarkdownFormatter.ToHtml("<script>x()</script>\n| <b>h</b> |\n|---|\n| <img src=x onerror=alert(1)> |");

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<th>&lt;b&gt;h&lt;/b&gt;</th>", html);
        Assert.Contains("<td>&lt;img src=x onerror=alert(1)&gt;</td>", html);
    }

    [Fact]
    public void ToHtml_KeepsTheOriginalInlineFormattingForTextWithoutTables()
    {
        Assert.Equal(
            "<strong>Plan</strong><br>Focus on <em>one</em> task, run <code>git status</code> &amp; rest.",
            ChatMarkdownFormatter.ToHtml("**Plan**\nFocus on *one* task, run `git status` & rest."));
    }

    [Fact]
    public void ToHtml_ReturnsEmptyForNullOrEmpty()
    {
        Assert.Equal(string.Empty, ChatMarkdownFormatter.ToHtml(null));
        Assert.Equal(string.Empty, ChatMarkdownFormatter.ToHtml(string.Empty));
    }

    #endregion
}
