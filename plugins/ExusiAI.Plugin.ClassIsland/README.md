# ClassIsland in ExusiAI

This plugin loads the original ClassIsland Avalonia desktop assembly from the bundled `NativeClassIsland` directory into the ExusiAI process. Its original information island, components, theme effects and settings are used directly. The ExusiAI page contains only controls to show or hide the original island and open the original settings. It has no alternate WPF renderer or imitation settings workbench.

The original assemblies are built from the checked-in `third_party/ClassIsland` source during Release builds. No separate ClassIsland executable is packaged, started or downloaded. Native data is isolated in `%LocalAppData%\ExusiAI\ClassIsland`; the embedded application does not create a second tray icon. See `THIRD_PARTY_NOTICES.md` for upstream source and license details.
