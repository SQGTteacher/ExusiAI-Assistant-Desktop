# ExusiAI ClassIsland plugin

ClassIsland 2.2 Misha is implemented as a native ExusiAI plugin. The normal execution path does not launch or download a separate ClassIsland process.

An experimental in-process Avalonia information island runs on a dedicated STA UI
thread. Set `EXUSIAI_CLASSISLAND_AVALONIA=1` before launching to preview it;
initialization failure falls back to WPF. The bridge renders native Avalonia
component rows, recursively builds group/stack/slide containers, updates text
without recreating controls each second, sizes to content and saves drag offsets.
It also uses Avalonia's native conic gradient for the Liquid Glass edge. It does
not yet reproduce upstream `MainWindowLine`, `ComponentPresenter`, all five
Liquid Glass optical layers, rolling transitions or notification animations.
Keep WPF as the default until Windows build and visual checks pass.

The complete available ClassIsland 2.2 source snapshot, including its originally
missing EdgeTtsSharp project, fonts, built-in themes, localization and static
assets, is staged under `third_party/ClassIsland`. The original desktop app now
has an embedded entry point, an isolated data directory, and an in-process
reflection bridge. Build `third_party/ClassIsland/ClassIsland.Desktop` with its
`.NET 9` SDK and Windows publish properties first; the ExusiAI Release build
then packages that output under `NativeClassIsland` and uses the original
Avalonia UI and settings by default. The Windows CI checks both builds. A
native Windows launch and visual check is still required. Online theme/plugin
catalogs and weather remain optional network-backed features, not startup
dependencies. The legacy 2.1 runtime build still has a download fallback while
that migration is unfinished.

## Core services

- `ClassIslandProfileService` reads and writes the upstream `Profile` JSON shape. GUID-keyed `Subjects`, `TimeLayouts` and `ClassPlans` remain compatible, and unknown fields are retained at every modeled level.
- `ClassIslandTimetableService` resolves `TimeRule` weekly, rotating-week, date and loop rules and maps class entries to their subjects and time-layout items.
- `ClassIslandProfileService` also implements upstream class-plan groups, ordered schedules, temporary overlay plans and migration records, including the protected default/global groups.
- `ClassIslandTimetableService` honors active overlay plans and date-specific `OrderedSchedules`, and exposes Schedule-mode items.
- `ClassIslandComponentService` reads and writes the upstream `ComponentProfile -> Lines -> ComponentSettings` structure under `Config/ComponentLayouts`, including visual overrides, size/margin constraints, rules and unknown component settings.
- `ClassIslandNotificationService` queues mask/overlay requests, tracks timing and cancellation, and resumes queued requests after a host restart. The island displays basic notification text; upstream templates, speech, sound and action execution still require migration.
- `ClassIslandCoreService` creates, starts and stops these modules as one unit under the ExusiAI plugin lifecycle.
- `ClassIslandPresentationService` owns the in-process information-island overlay. Its appearance is stored separately from the ExusiAI theme, with a compact 440×52 default, six dock positions, mouse-hover fading, and a tool-window style that stays out of Alt+Tab. Selected component layouts drive date, schedule, clock, text, countdown, cached/refreshed weather, and nested group/stack/rolling/slide presenters. Weather accepts ClassIsland city identifiers and refreshes from a public Open-Meteo endpoint without the upstream provider's embedded signature; the public endpoint does not supply ClassIsland's alert data. The SQGT Liquid Glass Crystal brushes now use all five source layers and both source palettes; its conic rim is sampled into WPF drawing wedges. The original Avalonia renderer and multi-shadow behavior still require visual comparison on Windows. Rules, full weather alerts, container transition behavior, all original settings pages and upstream notification visuals still need migration.

## Integrated workbench

The plugin page now follows the same full-workbench approach as the ArkPets integration instead of presenting synchronization as the whole product. Its continuous navigation exposes the live information island, Profile/subject/time-layout/class-plan editing, component lines and the upstream built-in component catalog, v2 notification playback, appearance/position settings, and transfer/synchronization. All controls use ExusiAI theme resources and edit the same service instances used by the running island.

## Data and transfer

The plugin stores writable data in `%LocalAppData%/ExusiAI/ClassIsland` and remains portable as an ordinary ExusiAI package. It can import/export individual upstream-compatible Profile JSON files and import the core configuration (`Settings.json`, `Profiles/`, `Config/`) from ClassIsland backup ZIP files or an existing ClassIsland Data folder. The original folder is only read; import uses the same validated staging and rollback path as backups. This is not a full backup migration of plugin assets, themes or rule dependencies. `SelectedProfile` and `CurrentComponentConfig` in the imported Settings are used when loading data.

Overlay width and height are physical screen pixels; they are converted to WPF DIPs for each monitor's DPI scale. The settings page limits content width and controls to usable classroom proportions rather than stretching every button across the host.

The basic settings page reads and saves `SingleWeekStartTime` for rotating week rules. The clock page applies `TimeOffsetSeconds` to the island and timetable while preserving unrelated `Settings.json` fields. The upstream NTP client, privacy, storage, automation, update and plugin-management settings pages remain unported.

The GUID-keyed `AttachedObjects` on upstream attachable profile models can be read and edited without discarding unknown settings. Registered rule, notification and action settings still need typed adapters and execution. Legacy Subject fields outside the upstream model are preserved as unknown data rather than rewritten as 2.1 fields. The workbench and island remain partial WPF implementations, not a screen-for-screen migration of the Avalonia UI.

The earlier `ClassIslandRuntimeHost` implementation remains source-compatible for legacy callers during the transition, but it is no longer constructed by the plugin, included in package assets, or used for new features.

## Upstream baseline

- Repository: `ClassIsland/ClassIsland`
- Misha branch: `develop/v2/misha-alpha`
- Reviewed baseline: `08808615899d1a4abb8e0ef576bf1e247adde10f`

See `THIRD_PARTY_NOTICES.md` for attribution and source availability.
