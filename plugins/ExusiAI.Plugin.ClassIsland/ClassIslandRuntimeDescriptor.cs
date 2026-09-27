using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ExusiAI.Plugin.ClassIsland;

internal static class ClassIslandRuntimeDescriptor
{
    public const string UpstreamRepository = "ClassIsland/ClassIsland";
    public const string RuntimeVersion = "2.1.0.1";
    public const string RuntimeReleaseCommit = "15273f82c9d2d55929df83b5fb806e68ee4547c0";
    public const string MishaBranch = "develop/v2/misha-alpha";
    public const string MishaBaselineCommit = "08808615899d1a4abb8e0ef576bf1e247adde10f";

    public const string LauncherFileName = "ClassIsland.exe";
    public const string AppFolderName = "app-2.1.0.1-0";
    public const string DesktopFileName = "ClassIsland.Desktop.exe";
    public const string PackageTypeFileName = "PackageType";

    public const string LauncherSha256 = "06e69ff08538c3f2c1e650914d3edfd3960f7fdda887e2d654551861f14757a8";
    public const string DesktopSha256 = "9854f9bced74f7213f16345b434a15b1771c9780483364e196fcb3fb64da0ecc";
    public const string SeedArchiveSha256 = "d0bb33c1e1b79edc147b75f4a79d6c3acc7b6a6965f1b45ee7854626ea351c94";

    public static string BundledRuntimeRoot => Path.Combine(
        Path.GetDirectoryName(typeof(ClassIslandPlugin).Assembly.Location)
        ?? throw new InvalidOperationException("无法确定 ClassIsland 插件目录。"),
        "Runtime");

    public static string ManagedRuntimeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExusiAI", "classisland", "runtime");

    public static string LauncherPath(string runtimeRoot) => Path.Combine(runtimeRoot, LauncherFileName);
    public static string AppDirectory(string runtimeRoot) => Path.Combine(runtimeRoot, AppFolderName);
    public static string DesktopPath(string runtimeRoot) => Path.Combine(AppDirectory(runtimeRoot), DesktopFileName);
    public static string DataDirectory(string runtimeRoot) => Path.Combine(runtimeRoot, "data");

    public static void ValidatePreparedRuntime(string runtimeRoot, bool verifyHashes = true)
    {
        var launcher = LauncherPath(runtimeRoot);
        var appDirectory = AppDirectory(runtimeRoot);
        var desktop = DesktopPath(runtimeRoot);
        var packageType = Path.Combine(appDirectory, PackageTypeFileName);

        if (!File.Exists(launcher)) throw new FileNotFoundException("ClassIsland 内置启动器不存在。", launcher);
        if (!Directory.Exists(appDirectory)) throw new DirectoryNotFoundException($"ClassIsland 版本目录不存在：{appDirectory}");
        if (!File.Exists(desktop)) throw new FileNotFoundException("ClassIsland.Desktop.exe 不存在，内置运行时不完整。", desktop);
        if (!File.Exists(packageType) || !string.Equals(File.ReadAllText(packageType).Trim(), "folder", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ClassIsland 运行时必须保持上游 folder 包结构。");

        var launcherVersion = FileVersionInfo.GetVersionInfo(launcher).FileVersion;
        var desktopVersion = FileVersionInfo.GetVersionInfo(desktop).FileVersion;
        if (!VersionMatches(launcherVersion) || !VersionMatches(desktopVersion))
            throw new InvalidDataException($"ClassIsland 内置运行时版本不匹配。期望 {RuntimeVersion}，实际 launcher={launcherVersion ?? "?"}, desktop={desktopVersion ?? "?"}。");

        if (!verifyHashes) return;
        VerifySha256(launcher, LauncherSha256, "ClassIsland.exe");
        VerifySha256(desktop, DesktopSha256, "ClassIsland.Desktop.exe");
    }

    private static void VerifySha256(string path, string expected, string displayName)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{displayName} SHA-256 校验失败；拒绝启动被替换的内置运行时。");
    }

    private static bool VersionMatches(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Equals(RuntimeVersion, StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith(RuntimeVersion + "+", StringComparison.OrdinalIgnoreCase));
}
