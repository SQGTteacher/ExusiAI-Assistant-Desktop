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
