namespace ExusiAI.FileViewer.Core;

public readonly record struct DocumentPageWindow(int StartPage, int EndPage, int TargetPage)
{
    public int Count => EndPage - StartPage + 1;
}

public static class DocumentPageWindowPlanner
{
    public static DocumentPageWindow Initial(int totalPages, int initialPages = 20)
    {
        Validate(totalPages, initialPages);
        return new(1, Math.Min(totalPages, initialPages), 1);
    }

    public static DocumentPageWindow Around(int totalPages, int targetPage, int radius = 10)
    {
        if (totalPages < 1) throw new ArgumentOutOfRangeException(nameof(totalPages));
        if (targetPage < 1 || targetPage > totalPages) throw new ArgumentOutOfRangeException(nameof(targetPage));
        if (radius < 0 || radius > 50) throw new ArgumentOutOfRangeException(nameof(radius));

        var desiredCount = Math.Min(totalPages, checked(radius * 2 + 1));
        var start = Math.Max(1, targetPage - radius);
        var end = Math.Min(totalPages, targetPage + radius);
        if (end - start + 1 < desiredCount)
        {
            if (start == 1) end = desiredCount;
            else start = totalPages - desiredCount + 1;
        }
        return new(start, end, targetPage);
    }

    private static void Validate(int totalPages, int initialPages)
    {
        if (totalPages < 1) throw new ArgumentOutOfRangeException(nameof(totalPages));
        if (initialPages < 1 || initialPages > 100) throw new ArgumentOutOfRangeException(nameof(initialPages));
    }
}
