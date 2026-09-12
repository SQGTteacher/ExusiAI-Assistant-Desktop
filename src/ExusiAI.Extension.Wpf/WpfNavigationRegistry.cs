using System.Collections.Immutable;

namespace ExusiAI.Extension.Wpf;

public sealed record RegisteredWpfPage(string PackageId, WpfNavigationPage Page);

public sealed class WpfNavigationRegistry
{
    private readonly List<RegisteredWpfPage> pages = [];

    public IReadOnlyList<RegisteredWpfPage> Pages => pages.ToImmutableArray();
    public event EventHandler? Changed;

    public void Register(string packageId, IEnumerable<WpfNavigationPage> packagePages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(packagePages);
        var candidates = packagePages.ToArray();
        foreach (var page in candidates)
        {
            if (string.IsNullOrWhiteSpace(page.Route) || string.IsNullOrWhiteSpace(page.Title) || page.CreateView is null)
                throw new ArgumentException("Navigation pages require a route, title, and view factory.", nameof(packagePages));
            if (page.Route.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.')))
                throw new ArgumentException($"Navigation route '{page.Route}' is invalid.", nameof(packagePages));
            if (pages.Any(x => string.Equals(x.Page.Route, page.Route, StringComparison.OrdinalIgnoreCase)) ||
                candidates.Count(x => string.Equals(x.Route, page.Route, StringComparison.OrdinalIgnoreCase)) > 1)
                throw new InvalidOperationException($"Navigation route '{page.Route}' is already registered.");
        }

        pages.AddRange(candidates.Select(page => new RegisteredWpfPage(packageId, page)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Unregister(string packageId)
    {
        if (pages.RemoveAll(x => string.Equals(x.PackageId, packageId, StringComparison.OrdinalIgnoreCase)) > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
