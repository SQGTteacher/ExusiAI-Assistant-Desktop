using System.IO;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandRuntimeHost
{
    private readonly IExtensionLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string bundledRoot;
    private ClassIslandProcessJob? processJob;

    public ClassIslandRuntimeHost(IExtensionLogger logger, string? runtimeRoot = null, string? bundledRuntimeRoot = null)
    {
        this.logger = logger;
        RuntimeRoot = Path.GetFullPath(runtimeRoot ?? ClassIslandRuntimeDescriptor.ManagedRuntimeRoot);
        bundledRoot = Path.GetFullPath(bundledRuntimeRoot ?? ClassIslandRuntimeDescriptor.BundledRuntimeRoot);
    }

    public string RuntimeRoot { get; }
    public string DataDirectory => ClassIslandRuntimeDescriptor.DataDirectory(RuntimeRoot);
    public bool IsRunning
    {
        get
        {
            try { return processJob?.HasActiveProcesses() == true; }
            catch { return false; }
        }
    }

    public event EventHandler? StatusChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClassIslandRuntimeInstaller.EnsureInstalled(bundledRoot, RuntimeRoot);
        Directory.CreateDirectory(DataDirectory);
        logger.Information($"ClassIsland {ClassIslandRuntimeDescriptor.RuntimeVersion} managed runtime verified at '{RuntimeRoot}'.");
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (processJob?.HasActiveProcesses() == true) return;
            processJob?.Dispose();
            processJob = null;
            ClassIslandRuntimeDescriptor.ValidatePreparedRuntime(RuntimeRoot);
            var job = new ClassIslandProcessJob();
            try
            {
                job.StartLauncher(ClassIslandRuntimeDescriptor.LauncherPath(RuntimeRoot), RuntimeRoot);
                processJob = job;
                logger.Information($"Started bundled ClassIsland {ClassIslandRuntimeDescriptor.RuntimeVersion}.");
            }
            catch { job.Dispose(); throw; }
        }
        finally { gate.Release(); StatusChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await StopCoreAsync(cancellationToken); }
        finally { gate.Release(); StatusChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
            ClassIslandRuntimeDescriptor.ValidatePreparedRuntime(RuntimeRoot);
            var job = new ClassIslandProcessJob();
            try
            {
                job.StartLauncher(ClassIslandRuntimeDescriptor.LauncherPath(RuntimeRoot), RuntimeRoot);
                processJob = job;
            }
            catch { job.Dispose(); throw; }
        }
        finally { gate.Release(); StatusChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task<ClassIslandBackupSummary> SyncBackupAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var restart = processJob?.HasActiveProcesses() == true;
            if (restart) await StopCoreAsync(cancellationToken);
            var result = await ClassIslandBackupImporter.ImportIntoDataDirectoryAsync(archivePath, DataDirectory, cancellationToken);
            logger.Information($"Synchronized ClassIsland backup '{Path.GetFileName(archivePath)}' into native data.");
            if (restart)
            {
                var job = new ClassIslandProcessJob();
                try
                {
                    job.StartLauncher(ClassIslandRuntimeDescriptor.LauncherPath(RuntimeRoot), RuntimeRoot);
                    processJob = job;
                }
                catch { job.Dispose(); throw; }
            }
            return result;
        }
        finally { gate.Release(); StatusChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (processJob is null) return;
        var job = processJob;
        processJob = null;
        try
        {
            if (job.HasActiveProcesses())
            {
                job.RequestGracefulClose();
                for (var i = 0; i < 15 && job.HasActiveProcesses(); i++)
                    await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            job.Dispose();
            logger.Information("Stopped bundled ClassIsland runtime.");
        }
    }
}
