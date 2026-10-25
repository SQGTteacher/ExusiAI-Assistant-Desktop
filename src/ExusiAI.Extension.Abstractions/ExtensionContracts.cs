namespace ExusiAI.Extension.Abstractions;

public interface IExtensionLogger
{
    void Information(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public interface IExtensionContext
{
    string PackageId { get; }
    IExtensionLogger Logger { get; }
}

// Optional package location for plugins that load files alongside their assemblies.
public interface IExtensionPackageContext : IExtensionContext
{
    string PackageDirectory { get; }
}

public interface IExusiAIPlugin
{
    Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Optional lifecycle for plugins that must retain process-scoped native state
/// while disabling all user-visible and background behavior.
/// </summary>
public interface ISuspendablePlugin : IExusiAIPlugin
{
    Task SuspendAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
}
