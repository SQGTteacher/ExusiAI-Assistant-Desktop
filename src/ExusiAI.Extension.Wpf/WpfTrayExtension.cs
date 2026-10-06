namespace ExusiAI.Extension.Wpf;

/// <summary>A command contributed to the host's existing tray menu.</summary>
public sealed record WpfTrayCommand(string Id, string Title, Func<CancellationToken, Task> ExecuteAsync);

public interface IWpfTrayExtension
{
    IReadOnlyCollection<WpfTrayCommand> GetTrayCommands();
}

public sealed record RegisteredWpfTrayMenu(string PackageId, string Title, IReadOnlyList<WpfTrayCommand> Commands);

/// <summary>Package-scoped menu contributions, safe to read from the UI thread.</summary>
public sealed class WpfTrayRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, RegisteredWpfTrayMenu> menus = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public IReadOnlyList<RegisteredWpfTrayMenu> Menus
    {
        get { lock (gate) return menus.Values.ToArray(); }
    }

    public void Register(string packageId, string title, IEnumerable<WpfTrayCommand> commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(commands);
        var candidates = commands.ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in candidates)
        {
            if (command is null || string.IsNullOrWhiteSpace(command.Id) ||
                string.IsNullOrWhiteSpace(command.Title) || command.ExecuteAsync is null)
                throw new ArgumentException("Tray commands require an id, title, and action.", nameof(commands));
            if (!ids.Add(command.Id)) throw new ArgumentException("Duplicate tray command id.", nameof(commands));
        }
        lock (gate)
            menus[packageId] = new(packageId, title, Array.AsReadOnly(candidates));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRegistered(string packageId, WpfTrayCommand command)
    {
        lock (gate) return menus.TryGetValue(packageId, out var menu) &&
            menu.Commands.Any(candidate => ReferenceEquals(candidate, command));
    }

    public void Unregister(string packageId)
    {
        bool removed;
        lock (gate) removed = menus.Remove(packageId);
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }
}
