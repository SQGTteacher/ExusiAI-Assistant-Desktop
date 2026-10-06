namespace ExusiAI.Extension.Wpf;

/// <summary>
/// The part of the shell theme that a UI extension adopts so the two rendering
/// stacks do not drift apart.
/// </summary>
/// <remarks>
/// Only the variant and the accent cross the process boundary. Extension UI keeps
/// its own design language, because upstream visual fidelity depends on it: a shell
/// that pushed its whole palette into an extension would silently re-skin it.
/// </remarks>
public sealed record HostTheme(bool IsDark, string Accent);

/// <summary>
/// Implemented by UI extensions that follow the shell theme. The coordinator pushes
/// the current theme on attach and again whenever the user changes it.
/// </summary>
public interface IWpfHostThemeExtension
{
    void ApplyHostTheme(HostTheme theme);
}
