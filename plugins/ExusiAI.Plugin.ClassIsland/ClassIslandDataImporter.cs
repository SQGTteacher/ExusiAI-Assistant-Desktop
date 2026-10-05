using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>Copies upstream user data into the isolated embedded data root.</summary>
internal static class ClassIslandDataImporter
{
    private static readonly string[] ManagedNames = ["Settings.json", "Profiles", "Config"];

    internal static Task<string> ImportAsync(string sourceDirectory, string dataDirectory,
        Action stopHost, Func<bool> isHostStopped, CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(sourceDirectory, dataDirectory, stopHost, isHostStopped, cancellationToken), cancellationToken);

    private static string Import(string sourceDirectory, string dataDirectory,
        Action stopHost, Func<bool> isHostStopped, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(dataDirectory);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择原版 ClassIsland 的 Data 文件夹。");
        var sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationPrefix = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("原版目录与 ExusiAI 数据目录不能互相包含。");
        if (Directory.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("ExusiAI 数据目录是链接，导入已取消。");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
            !File.Exists(Path.Combine(source, "Settings.json")))
            throw new InvalidDataException("所选目录不是有效的 ClassIsland Data 文件夹。");

        var parent = Directory.GetParent(destination)?.FullName
            ?? throw new InvalidOperationException("目标数据目录无有效父目录。");
        Directory.CreateDirectory(parent);
        var token = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, "ClassIsland-import-" + token);
        var backup = Path.Combine(parent, "ClassIsland-before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + token);
        Directory.CreateDirectory(staging);
        var movedExisting = new List<string>();
        var movedNew = new List<string>();
        var applied = false;
        try
        {
            foreach (var name in ManagedNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = Path.Combine(source, name);
                var stagedPath = Path.Combine(staging, name);
                if ((File.Exists(sourcePath) || Directory.Exists(sourcePath)) &&
                    (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"数据目录包含链接，导入已取消：{sourcePath}");
                if (File.Exists(sourcePath)) File.Copy(sourcePath, stagedPath);
                else if (Directory.Exists(sourcePath)) CopyDirectory(sourcePath, stagedPath, cancellationToken);
            }
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(staging, "Settings.json"))))
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Settings.json 的根节点必须是对象。");

            cancellationToken.ThrowIfCancellationRequested();
            stopHost();
            if (!isHostStopped())
                throw new IOException("原版信息岛尚未停止，导入已取消；请重启 ExusiAI 后再试。");

            Directory.CreateDirectory(destination);
            Directory.CreateDirectory(backup);
            try
            {
                foreach (var name in ManagedNames)
                {
                    var current = Path.Combine(destination, name);
                    if (!File.Exists(current) && !Directory.Exists(current)) continue;
                    Move(current, Path.Combine(backup, name));
                    movedExisting.Add(name);
                }
                foreach (var name in ManagedNames)
                {
                    var stagedPath = Path.Combine(staging, name);
                    if (!File.Exists(stagedPath) && !Directory.Exists(stagedPath)) continue;
                    Move(stagedPath, Path.Combine(destination, name));
                    movedNew.Add(name);
                }
                applied = true;
            }
            catch
            {
                foreach (var name in movedNew)
                    Delete(Path.Combine(destination, name));
                foreach (var name in movedExisting)
                    Move(Path.Combine(backup, name), Path.Combine(destination, name));
                throw;
            }
            return backup;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (!applied && Directory.Exists(backup) && !Directory.EnumerateFileSystemEntries(backup).Any())
                Directory.Delete(backup);
        }
    }

    private static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"数据目录包含链接，导入已取消：{entry}");
            var destination = Path.Combine(target, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyDirectory(entry, destination, cancellationToken);
            else File.Copy(entry, destination);
        }
    }

    private static void Move(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target);
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
