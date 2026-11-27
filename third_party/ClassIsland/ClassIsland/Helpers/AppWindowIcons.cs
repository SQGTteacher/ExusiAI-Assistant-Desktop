using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ClassIsland.Helpers;

internal static class AppWindowIcons
{
    // Avalonia owns the per-window copies. Reloading the embedded service
    // session can reuse its original information-island source icon.
    private static readonly Lazy<WindowIcon> ico = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://ClassIsland/Assets/AppLogo.ico"));
        return new WindowIcon(stream);
    });

    public static WindowIcon Ico => ico.Value;
}
