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
}
