namespace ExusiAI.Core;

public static class ApplicationInfo
{
    public const string ProductName = "ExusiAI Assistant Desktop";
    public const string Version = "0.2.0-preview.1";
    public const string ExtensionApiVersion = "1";

    public static string BuildNumber
    {
        get
        {
            var informationalVersion = typeof(ApplicationInfo).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            const string marker = "+build.";
            var markerIndex = informationalVersion?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
            if (markerIndex < 0) return "local";
            var value = informationalVersion![(markerIndex + marker.Length)..];
            var separatorIndex = value.IndexOfAny(['+', '.']);
            return separatorIndex > 0 ? value[..separatorIndex] : value;
        }
    }

    public static string PreviewLabel => $"{Version} · build {BuildNumber}";
}
