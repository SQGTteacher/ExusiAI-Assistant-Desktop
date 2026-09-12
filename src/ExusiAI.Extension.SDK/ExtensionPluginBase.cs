using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Extension.SDK;

public abstract class ExtensionPluginBase : IExusiAIPlugin
{
    protected IExtensionContext Context { get; private set; } = null!;

    public virtual Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Context = context;
        return Task.CompletedTask;
    }

    public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
