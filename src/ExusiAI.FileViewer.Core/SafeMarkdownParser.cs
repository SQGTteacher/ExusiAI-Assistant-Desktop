using System.Collections.Immutable;
using System.Text;

namespace ExusiAI.FileViewer.Core;

public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    UnorderedListItem,
    OrderedListItem,
    Quote,
    Code,
    Rule
}

public enum MarkdownInlineKind
{
    Text,
    Bold,
    Italic,
    Code
}

public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text);
public sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, int Level, int SourceLine);
public sealed record MarkdownParseResult(ImmutableArray<MarkdownBlock> Blocks, bool IsTruncated, int ParsedCharacters);

public static class SafeMarkdownParser
{
    public const int DefaultMaximumCharacters = 1024 * 1024;
    public const int DefaultMaximumBlocks = 2_000;

    public static MarkdownParseResult Parse(
        string markdown,
        int maximumCharacters = DefaultMaximumCharacters,
        int maximumBlocks = DefaultMaximumBlocks)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (maximumCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        if (maximumBlocks <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBlocks));

        var parsedCharacters = Math.Min(markdown.Length, maximumCharacters);
        if (parsedCharacters < markdown.Length && parsedCharacters > 0 && char.IsHighSurrogate(markdown[parsedCharacters - 1]))
            parsedCharacters--;
        var source = markdown[..parsedCharacters]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = source.Split('\n');
        var blocks = ImmutableArray.CreateBuilder<MarkdownBlock>();
        var paragraph = new StringBuilder();
        var paragraphLine = 1;
        var code = new StringBuilder();
        var codeLine = 1;
        var inCode = false;
        var processedLines = 0;

        for (var index = 0; index < lines.Length && blocks.Count < maximumBlocks; index++)
        {
            processedLines = index + 1;
            var lineNumber = index + 1;
            var line = lines[index];
            var trimmed = line.Trim();

            if (IsFence(trimmed))
            {
                FlushParagraph();
                if (inCode)
                {
                    AddBlock(new(MarkdownBlockKind.Code, code.ToString().TrimEnd('\n'), 0, codeLine));
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                    codeLine = lineNumber + 1;
                }
                continue;
            }

            if (inCode)
            {
                code.AppendLine(line);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (TryReadHeading(trimmed, out var level, out var heading))
            {
                FlushParagraph();
                AddBlock(new(MarkdownBlockKind.Heading, heading, level, lineNumber));
            }
            else if (IsRule(trimmed))
            {
                FlushParagraph();
                AddBlock(new(MarkdownBlockKind.Rule, string.Empty, 0, lineNumber));
            }
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                FlushParagraph();
                AddBlock(new(MarkdownBlockKind.Quote, trimmed.Length > 1 ? trimmed[1..].TrimStart() : string.Empty, 0, lineNumber));
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                     trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                     trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddBlock(new(MarkdownBlockKind.UnorderedListItem, trimmed[2..], 0, lineNumber));
            }
            else if (TryReadOrderedItem(trimmed, out var number, out var item))
            {
                FlushParagraph();
                AddBlock(new(MarkdownBlockKind.OrderedListItem, item, number, lineNumber));
            }
            else
            {
                if (paragraph.Length == 0) paragraphLine = lineNumber;
                else paragraph.Append(' ');
                paragraph.Append(trimmed);
            }
        }

        if (blocks.Count < maximumBlocks)
        {
            if (inCode && code.Length > 0)
                AddBlock(new(MarkdownBlockKind.Code, code.ToString().TrimEnd('\n'), 0, codeLine));
            else
                FlushParagraph();
        }

        var truncated = parsedCharacters < markdown.Length || processedLines < lines.Length;
        return new(blocks.ToImmutable(), truncated, parsedCharacters);

        void FlushParagraph()
        {
            if (paragraph.Length == 0 || blocks.Count >= maximumBlocks) return;
            AddBlock(new(MarkdownBlockKind.Paragraph, paragraph.ToString(), 0, paragraphLine));
            paragraph.Clear();
        }

        void AddBlock(MarkdownBlock block)
        {
            if (blocks.Count < maximumBlocks) blocks.Add(block);
        }
    }

    public static ImmutableArray<MarkdownInline> ParseInlines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = ImmutableArray.CreateBuilder<MarkdownInline>();
        var plainStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            var markerLength = index + 1 < text.Length && text[index] == '*' && text[index + 1] == '*' ? 2 : 1;
            var kind = markerLength == 2
                ? MarkdownInlineKind.Bold
                : text[index] == '`'
                    ? MarkdownInlineKind.Code
                    : text[index] is '*' or '_'
                        ? MarkdownInlineKind.Italic
                        : MarkdownInlineKind.Text;
            if (kind == MarkdownInlineKind.Text)
            {
                index++;
                continue;
            }

            var marker = text.Substring(index, markerLength);
            var close = text.IndexOf(marker, index + markerLength, StringComparison.Ordinal);
            if (close <= index + markerLength)
            {
                index += markerLength;
                continue;
            }

            if (index > plainStart) result.Add(new(MarkdownInlineKind.Text, text[plainStart..index]));
            result.Add(new(kind, text[(index + markerLength)..close]));
            index = close + markerLength;
            plainStart = index;
        }

        if (plainStart < text.Length) result.Add(new(MarkdownInlineKind.Text, text[plainStart..]));
        return result.ToImmutable();
    }

    private static bool IsFence(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal);

    private static bool TryReadHeading(string line, out int level, out string heading)
    {
        level = 0;
        while (level < line.Length && level < 6 && line[level] == '#') level++;
        if (level == 0 || level >= line.Length || line[level] != ' ')
        {
            heading = string.Empty;
            return false;
        }
        heading = line[(level + 1)..].Trim();
        return heading.Length > 0;
    }

    private static bool TryReadOrderedItem(string line, out int number, out string item)
    {
        var separator = line.IndexOf(". ", StringComparison.Ordinal);
        if (separator is < 1 or > 9 || !int.TryParse(line[..separator], out number))
        {
            item = string.Empty;
            return false;
        }
        item = line[(separator + 2)..];
        return true;
    }

    private static bool IsRule(string line)
    {
        var compact = line.Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length >= 3 && compact.All(character => character == compact[0]) && compact[0] is '-' or '*' or '_';
    }
}
