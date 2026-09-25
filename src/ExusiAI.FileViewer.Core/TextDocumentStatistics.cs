namespace ExusiAI.FileViewer.Core;

public sealed record TextDocumentStatistics(int Lines, int Words, int Characters)
{
    public static TextDocumentStatistics Calculate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Length == 0 ? 0 : 1;
        var words = 0;
        var insideWord = false;

        foreach (var character in text)
        {
            if (character == '\n') lines++;

            if (char.IsWhiteSpace(character))
            {
                insideWord = false;
            }
            else if (!insideWord)
            {
                words++;
                insideWord = true;
            }
        }

        return new(lines, words, text.Length);
    }

    public static TextDocumentPosition Locate(string text, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        var target = Math.Clamp(characterIndex, 0, text.Length);
        var line = 1;
        var column = 1;
        for (var index = 0; index < target; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return new(line, column);
    }

    public static int GetLineStart(string text, int oneBasedLine)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (oneBasedLine <= 1) return 0;

        var line = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            line++;
            if (line == oneBasedLine) return index + 1;
        }
        return text.Length;
    }
}

public sealed record TextDocumentPosition(int Line, int Column);
