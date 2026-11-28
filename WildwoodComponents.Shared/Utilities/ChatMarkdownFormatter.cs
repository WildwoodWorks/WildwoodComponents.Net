using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Turns an AI chat message (lightweight markdown) into display HTML.
///
/// Everything is HTML-encoded before any markup is added, so model output can never inject
/// tags. Text outside tables keeps the chat's long-standing inline rules exactly
/// (<c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, newline to <c>&lt;br&gt;</c>), and GitHub-flavored
/// pipe tables become real <c>&lt;table&gt;</c> elements instead of raw <c>| a | b |</c> /
/// <c>|---|---|</c> lines.
///
/// A table is a header row containing a pipe, immediately followed by a delimiter row whose
/// cells are only hyphens and optional colons (<c>---</c>, <c>:--</c>, <c>--:</c>, <c>:-:</c>) with
/// the same column count, then body rows until a blank or pipe-less line. Lines inside
/// <c>```</c> fenced code blocks are never treated as tables. No LINQ (iOS rule).
/// </summary>
public static class ChatMarkdownFormatter
{
    // Compiled instances rather than [GeneratedRegex]: this library also targets netstandard2.0.
    private static readonly Regex BoldPattern = new(@"\*\*(.*?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicPattern = new(@"\*(.*?)\*", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new(@"`(.*?)`", RegexOptions.Compiled);

    private enum ColumnAlignment
    {
        None,
        Left,
        Center,
        Right
    }

    /// <summary>
    /// Converts chat message content to HTML. Null or empty input returns an empty string.
    /// </summary>
    public static string ToHtml(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = content!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new StringBuilder();
        var pending = new List<string>();
        var inFence = false;
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (IsFenceLine(line))
            {
                inFence = !inFence;
                pending.Add(line);
                i++;
                continue;
            }

            if (!inFence && i + 1 < lines.Length && TryReadTableHeader(line, lines[i + 1], out var header, out var alignments))
            {
                FlushText(output, pending, trimTrailingBlankLines: true);

                var bodyRows = new List<List<string>>();
                var j = i + 2;
                while (j < lines.Length && IsBodyRow(lines[j]))
                {
                    bodyRows.Add(SplitCells(lines[j]));
                    j++;
                }

                AppendTable(output, header, alignments, bodyRows);

                // A blank line right after the table is only separation; the table is a block.
                if (j < lines.Length && IsBlank(lines[j]))
                {
                    j++;
                }

                i = j;
                continue;
            }

            pending.Add(line);
            i++;
        }

        FlushText(output, pending, trimTrailingBlankLines: false);
        return output.ToString();
    }

    /// <summary>
    /// The chat's original inline formatting, applied to a run of non-table text.
    /// Kept byte-for-byte identical to the pre-table behavior so messages without tables
    /// render exactly as before.
    /// </summary>
    private static string FormatInline(string text)
    {
        text = WebUtility.HtmlEncode(text);
        text = text.Replace("\n", "<br>");
        text = BoldPattern.Replace(text, "<strong>$1</strong>");
        text = ItalicPattern.Replace(text, "<em>$1</em>");
        text = CodePattern.Replace(text, "<code>$1</code>");
        return text;
    }

    private static void FlushText(StringBuilder output, List<string> pending, bool trimTrailingBlankLines)
    {
        var count = pending.Count;
        if (trimTrailingBlankLines)
        {
            while (count > 0 && IsBlank(pending[count - 1]))
            {
                count--;
            }
        }

        if (count > 0)
        {
            var text = new StringBuilder();
            for (var k = 0; k < count; k++)
            {
                if (k > 0)
                {
                    text.Append('\n');
                }
                text.Append(pending[k]);
            }
            output.Append(FormatInline(text.ToString()));
        }

        pending.Clear();
    }

    private static void AppendTable(
        StringBuilder output,
        List<string> header,
        List<ColumnAlignment> alignments,
        List<List<string>> bodyRows)
    {
        var columns = header.Count;

        output.Append("<div class=\"ai-chat-table-wrap\"><table class=\"ai-chat-table\"><thead><tr>");
        for (var c = 0; c < columns; c++)
        {
            AppendCell(output, "th", header[c], alignments[c]);
        }
        output.Append("</tr></thead><tbody>");

        foreach (var row in bodyRows)
        {
            output.Append("<tr>");
            for (var c = 0; c < columns; c++)
            {
                // Ragged rows: missing cells render empty, extra cells are dropped (GFM behavior).
                var cell = c < row.Count ? row[c] : string.Empty;
                AppendCell(output, "td", cell, alignments[c]);
            }
            output.Append("</tr>");
        }

        output.Append("</tbody></table></div>");
    }

    private static void AppendCell(StringBuilder output, string tag, string text, ColumnAlignment alignment)
    {
        output.Append('<').Append(tag);
        switch (alignment)
        {
            case ColumnAlignment.Left:
                output.Append(" style=\"text-align:left\"");
                break;
            case ColumnAlignment.Center:
                output.Append(" style=\"text-align:center\"");
                break;
            case ColumnAlignment.Right:
                output.Append(" style=\"text-align:right\"");
                break;
        }
        output.Append('>').Append(FormatInline(text)).Append("</").Append(tag).Append('>');
    }

    private static bool TryReadTableHeader(
        string headerLine,
        string delimiterLine,
        out List<string> header,
        out List<ColumnAlignment> alignments)
    {
        header = new List<string>();
        alignments = new List<ColumnAlignment>();

        if (!HasUnescapedPipe(headerLine) || !HasUnescapedPipe(delimiterLine))
        {
            return false;
        }

        var delimiterCells = SplitCells(delimiterLine);
        var parsedAlignments = new List<ColumnAlignment>();
        foreach (var cell in delimiterCells)
        {
            if (!TryParseDelimiterCell(cell, out var alignment))
            {
                return false;
            }
            parsedAlignments.Add(alignment);
        }

        var headerCells = SplitCells(headerLine);
        if (headerCells.Count == 0 || headerCells.Count != parsedAlignments.Count)
        {
            return false;
        }

        header = headerCells;
        alignments = parsedAlignments;
        return true;
    }

    private static bool TryParseDelimiterCell(string cell, out ColumnAlignment alignment)
    {
        alignment = ColumnAlignment.None;
        if (cell.Length == 0)
        {
            return false;
        }

        var leftColon = cell[0] == ':';
        var rightColon = cell[cell.Length - 1] == ':';
        var start = leftColon ? 1 : 0;
        var end = rightColon ? cell.Length - 1 : cell.Length;

        if (end <= start)
        {
            return false; // ":" or "::" alone has no hyphen
        }

        for (var k = start; k < end; k++)
        {
            if (cell[k] != '-')
            {
                return false;
            }
        }

        if (leftColon && rightColon)
        {
            alignment = ColumnAlignment.Center;
        }
        else if (rightColon)
        {
            alignment = ColumnAlignment.Right;
        }
        else if (leftColon)
        {
            alignment = ColumnAlignment.Left;
        }

        return true;
    }

    private static bool IsBodyRow(string line)
    {
        return !IsBlank(line) && !IsFenceLine(line) && HasUnescapedPipe(line);
    }

    /// <summary>
    /// Splits a table row into trimmed cells. Optional leading/trailing pipes are dropped and
    /// <c>\|</c> is a literal pipe inside a cell.
    /// </summary>
    private static List<string> SplitCells(string line)
    {
        var text = line.Trim();
        if (text.Length > 0 && text[0] == '|')
        {
            text = text.Substring(1);
        }
        if (text.Length > 0 && text[text.Length - 1] == '|' && !IsEscapedAt(text, text.Length - 1))
        {
            text = text.Substring(0, text.Length - 1);
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        for (var k = 0; k < text.Length; k++)
        {
            var ch = text[k];
            if (ch == '\\' && k + 1 < text.Length && text[k + 1] == '|')
            {
                current.Append('|');
                k++;
                continue;
            }
            if (ch == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static bool HasUnescapedPipe(string line)
    {
        for (var k = 0; k < line.Length; k++)
        {
            if (line[k] == '|' && !IsEscapedAt(line, k))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsEscapedAt(string text, int index)
    {
        return index > 0 && text[index - 1] == '\\';
    }

    private static bool IsFenceLine(string line)
    {
        return line.TrimStart().StartsWith("```", System.StringComparison.Ordinal);
    }

    private static bool IsBlank(string line)
    {
        return line.Trim().Length == 0;
    }
}
