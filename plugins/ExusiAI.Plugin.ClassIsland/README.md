# ClassIsland in ExusiAI

This plugin loads the original ClassIsland Avalonia desktop assembly from the bundled `NativeClassIsland` directory into the ExusiAI process. Its original information island, components, theme effects and settings are used directly. The ExusiAI page contains only controls to show or hide the original island and open the original settings. It has no alternate WPF renderer or imitation settings workbench.

The original assemblies are built from the checked-in `third_party/ClassIsland` source during Debug and Release builds. No separate ClassIsland executable is packaged, started or downloaded. Native data is isolated in `%LocalAppData%\ExusiAI\ClassIsland`; the embedded application does not create a second tray icon. See `THIRD_PARTY_NOTICES.md` for upstream source and license details.

To reuse local ClassIsland courses and settings, choose **导入本机 ClassIsland 数据** and select the original `%AppData%\ClassIsland\Data` folder. Close any separately running ClassIsland first. The importer stages `Settings.json`, `Profiles` and `Config`, validates the settings JSON, then stops the embedded application before replacing its data. Existing data is kept in a timestamped `ClassIsland-before-import-*` folder beside the embedded data directory. Restart ExusiAI to load the imported data.

ClassIsland uses its independent upstream theme by default. Enable **跟随 ExusiAI 深浅色与强调色** on the plugin page to follow the shell. Disable it to immediately restore upstream settings. The choice persists separately from imported ClassIsland data; theme packs remain managed by ClassIsland.
