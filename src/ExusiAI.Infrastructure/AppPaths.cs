namespace ExusiAI.Infrastructure;

public interface IAppPaths
{
    string ApplicationDirectory { get; }
    string UserDataDirectory { get; }
    string PackagesDirectory { get; }
    string LogsDirectory { get; }
    string SettingsDirectory { get; }
    string CacheDirectory { get; }
    string TempDirectory { get; }
}

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        ApplicationDirectory = AppContext.BaseDirectory;
        UserDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI");
        PackagesDirectory = Path.Combine(UserDataDirectory, "packages");
        LogsDirectory = Path.Combine(UserDataDirectory, "logs");
        SettingsDirectory = Path.Combine(UserDataDirectory, "settings");
        CacheDirectory = Path.Combine(UserDataDirectory, "cache");
        TempDirectory = Path.Combine(UserDataDirectory, "temp");
    }

    public string ApplicationDirectory { get; }
    public string UserDataDirectory { get; }
    public string PackagesDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsDirectory { get; }
    public string CacheDirectory { get; }
    public string TempDirectory { get; }

    public void EnsureDirectories()
    {
        foreach (var directory in new[]
        {
            UserDataDirectory, PackagesDirectory, LogsDirectory, SettingsDirectory, CacheDirectory, TempDirectory
        })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
