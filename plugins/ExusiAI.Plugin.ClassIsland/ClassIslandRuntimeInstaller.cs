using System.IO;
namespace ExusiAI.Plugin.ClassIsland;

/// <summary>
/// Copies the read-only bundled runtime seed into a stable per-user runtime directory.
/// Native ClassIsland writes its folder-package data beside the executable, so running directly
/// inside ExusiAI's synchronized plugin directory would make user data look like package drift.
/// </summary>
internal static class ClassIslandRuntimeInstaller
{
    public static void EnsureInstalled(string bundledRoot, string managedRoot)
    {
        ClassIslandRuntimeDescriptor.ValidatePreparedRuntime(bundledRoot);

        if (Directory.Exists(managedRoot))
        {
            try
            {
                ClassIslandRuntimeDescriptor.ValidatePreparedRuntime(managedRoot);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Replace only the runtime payload below; native data is preserved across the swap.
            }
        }

        var parent = Directory.GetParent(managedRoot)?.FullName
            ?? throw new InvalidOperationException("ClassIsland 托管运行目录无有效父目录。");
        Directory.CreateDirectory(parent);
        var token = Guid.NewGuid().ToString("N");
        var staging = managedRoot + ".install-" + token;
        var backup = managedRoot + ".backup-" + token;

        CopyRuntimePayload(bundledRoot, staging);
        var oldData = Path.Combine(managedRoot, "data");
        if (Directory.Exists(oldData))
            CopyDirectory(oldData, Path.Combine(staging, "data"));

        try
        {
            if (Directory.Exists(managedRoot)) Directory.Move(managedRoot, backup);
            Directory.Move(staging, managedRoot);
            ClassIslandRuntimeDescriptor.ValidatePreparedRuntime(managedRoot);
            TryDelete(backup);
        }
        catch
        {
            TryDelete(managedRoot);
            if (Directory.Exists(backup)) Directory.Move(backup, managedRoot);
            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static void CopyRuntimePayload(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (IsRuntimeData(relative)) continue;
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsRuntimeData(relative)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool IsRuntimeData(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("data", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
