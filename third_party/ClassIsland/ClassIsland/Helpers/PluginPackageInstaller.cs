using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace ClassIsland.Helpers;

internal static class PluginPackageInstaller
{
    private const int MaxEntryCount = 4096;
    private const long MaxExpandedBytes = 512L * 1024 * 1024;
    private const long MaxManifestBytes = 1024 * 1024;
    private static readonly char[] InvalidWindowsChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    internal static void Install(string packagePath, string pluginsRoot, string pluginId, string entranceAssembly)
    {
        ValidatePluginId(pluginId);
        var normalizedEntrance = NormalizeArchivePath(entranceAssembly, allowDirectory: false);
        var root = Path.GetFullPath(pluginsRoot);
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, pluginId);
        // Keep transaction directories outside PluginsRootPath: plugin discovery
        // enumerates every direct child and would otherwise load a staged copy
        // or backup after an interrupted startup.
        var transaction = Path.Combine(Path.GetDirectoryName(root)!, ".ClassIslandPluginTransactions", pluginId);
        var staged = Path.Combine(transaction, "staged");
        var previous = Path.Combine(transaction, "previous");
        RecoverInterruptedTransaction(transaction, target, previous);
        var hadPrevious = Directory.Exists(target);
        var wasDisabled = hadPrevious && File.Exists(Path.Combine(target, ".disabled"));
        var wasUninstalling = hadPrevious && File.Exists(Path.Combine(target, ".uninstall"));

        try
        {
            Directory.CreateDirectory(transaction);
            Directory.CreateDirectory(staged);
            ExtractSafely(packagePath, staged);
            var entrancePath = ResolveInside(staged, normalizedEntrance);
            if (!File.Exists(entrancePath))
                throw new InvalidDataException($"Plugin entrance assembly '{entranceAssembly}' is missing.");
            _ = AssemblyName.GetAssemblyName(entrancePath);

            // Package contents may not change an existing plugin's enabled or
            // pending-uninstall state. A newly installed package is never born
            // in the uninstalling state.
            File.Delete(Path.Combine(staged, ".disabled"));
            File.Delete(Path.Combine(staged, ".uninstall"));
            if (wasDisabled) File.WriteAllText(Path.Combine(staged, ".disabled"), "");
            if (wasUninstalling) File.WriteAllText(Path.Combine(staged, ".uninstall"), "");

            if (hadPrevious) Directory.Move(target, previous);
            try
            {
                Directory.Move(staged, target);
            }
            catch
            {
                if (hadPrevious && Directory.Exists(previous) && !Directory.Exists(target))
                    Directory.Move(previous, target);
                throw;
            }
            TryDeleteTransaction(transaction);
        }
        catch
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
            // If replacement failed after moving the old version, restore it.
            if (hadPrevious && Directory.Exists(previous) && !Directory.Exists(target))
                Directory.Move(previous, target);
            throw;
        }
    }

    internal static string ReadManifestText(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxManifestBytes)
            throw new InvalidDataException($"Plugin manifest exceeds {MaxManifestBytes} bytes.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void RecoverInterruptedTransaction(string transaction, string target, string previous)
    {
        if (!Directory.Exists(transaction)) return;
        if (!Directory.Exists(target) && Directory.Exists(previous))
            Directory.Move(previous, target);
        if (Directory.Exists(transaction)) Directory.Delete(transaction, recursive: true);
    }

    private static void TryDeleteTransaction(string transaction)
    {
        try
        {
            if (Directory.Exists(transaction)) Directory.Delete(transaction, recursive: true);
            var parent = Path.GetDirectoryName(transaction);
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
        catch (IOException)
        {
            // The canonical target is already installed. A later startup will
            // retry cleanup before processing another package for this plugin.
        }
        catch (UnauthorizedAccessException)
        {
            // Antivirus/indexers can temporarily retain handles on Windows.
        }
    }

    private static void ExtractSafely(string packagePath, string destination)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaxEntryCount)
            throw new InvalidDataException("Plugin package contains too many entries.");
        long expandedBytes = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException("Plugin package is too large when expanded.");
            RejectLinks(entry);
            var isDirectory = string.IsNullOrEmpty(entry.Name);
            var relative = NormalizeArchivePath(entry.FullName, isDirectory);
            if (!paths.Add(relative.TrimEnd('/')))
                throw new InvalidDataException($"Plugin package contains duplicate path '{entry.FullName}'.");
            var output = ResolveInside(destination, relative);
            EnsureNoReparseParents(destination, output);
            if (isDirectory)
            {
                Directory.CreateDirectory(output);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var source = entry.Open();
            using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }

    private static string NormalizeArchivePath(string value, bool allowDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Plugin package contains an empty path.");
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
            throw new InvalidDataException($"Plugin package path '{value}' is rooted.");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || (!allowDirectory && normalized.EndsWith('/')))
            throw new InvalidDataException($"Plugin package path '{value}' is invalid.");
        foreach (var segment in segments) ValidateSegment(segment, value);
        return string.Join('/', segments) + (allowDirectory ? "/" : "");
    }

    private static void ValidatePluginId(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.Contains('/') || pluginId.Contains('\\'))
            throw new InvalidDataException($"Plugin id '{pluginId}' is not a directory name.");
        ValidateSegment(pluginId, pluginId);
    }

    private static void ValidateSegment(string segment, string source)
    {
        if (segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.IndexOfAny(InvalidWindowsChars) >= 0 || segment.Any(char.IsControl) ||
            ReservedWindowsNames.Contains(Path.GetFileNameWithoutExtension(segment)))
            throw new InvalidDataException($"Plugin package path '{source}' is invalid on Windows.");
    }

    private static string ResolveInside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Plugin package path '{relative}' escapes its destination.");
        return fullPath;
    }

    private static void EnsureNoReparseParents(string root, string output)
    {
        var parent = Path.GetDirectoryName(output);
        while (parent is not null && parent.Length >= root.Length)
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Plugin package extraction encountered a reparse point.");
            if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) break;
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static void RejectLinks(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixType is 0xA000 or 0x6000 || (entry.ExternalAttributes & 0x400) != 0)
            throw new InvalidDataException($"Plugin package entry '{entry.FullName}' is a link.");
    }
}
