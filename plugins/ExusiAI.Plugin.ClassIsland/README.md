# ExusiAI ClassIsland plugin

ClassIsland 2.2 Misha is implemented as a native ExusiAI plugin. The normal execution path does not launch or download a separate ClassIsland process.

## Core services

- `ClassIslandProfileService` reads and writes the upstream `Profile` JSON shape. GUID-keyed `Subjects`, `TimeLayouts` and `ClassPlans` remain compatible, and unknown fields are retained at every modeled level.
- `ClassIslandTimetableService` resolves `TimeRule` weekly, rotating-week, date and loop rules and maps class entries to their subjects and time-layout items.
- `ClassIslandProfileService` also implements upstream class-plan groups, ordered schedules, temporary overlay plans and migration records, including the protected default/global groups.
- `ClassIslandTimetableService` honors active overlay plans and date-specific `OrderedSchedules`, and exposes Schedule-mode items.
- `ClassIslandComponentService` reads and writes the upstream `ComponentProfile -> Lines -> ComponentSettings` structure under `Config/ComponentLayouts`, including visual overrides, size/margin constraints, rules and unknown component settings.
- `ClassIslandNotificationService` queues mask/overlay requests, tracks timing and cancellation, and resumes queued requests after a host restart. The island displays basic notification text; upstream templates, speech, sound and action execution still require migration.
- `ClassIslandCoreService` creates, starts and stops these modules as one unit under the ExusiAI plugin lifecycle.
- `ClassIslandPresentationService` owns the in-process information-island overlay. Its appearance is stored separately from the ExusiAI theme, with a compact 440×52 default, six dock positions, mouse-hover fading, and a tool-window style that stays out of Alt+Tab. Selected component layouts drive date, schedule, clock, text, countdown, cached/refreshed weather, and nested group/stack/rolling/slide presenters. Weather accepts ClassIsland city identifiers and refreshes from a public Open-Meteo endpoint without the upstream provider's embedded signature; the public endpoint does not supply ClassIsland's alert data. The SQGT Liquid Glass option reuses the supplied optical palette in WPF; Avalonia's conic edge and acrylic shader are not yet reproduced. Rules, full weather alerts, container transition behavior, all original settings pages and upstream notification visuals still need migration.

## Integrated workbench

The plugin page now follows the same full-workbench approach as the ArkPets integration instead of presenting synchronization as the whole product. Its continuous navigation exposes the live information island, Profile/subject/time-layout/class-plan editing, component lines and the upstream built-in component catalog, v2 notification playback, appearance/position settings, and transfer/synchronization. All controls use ExusiAI theme resources and edit the same service instances used by the running island.

## Data and transfer

The plugin stores writable data in `%LocalAppData%/ExusiAI/ClassIsland` and remains portable as an ordinary ExusiAI package. It can import/export individual upstream-compatible Profile JSON files and import the core configuration (`Settings.json`, `Profiles/`, `Config/`) from ClassIsland backup ZIP files. This is not a full backup migration of plugin assets, themes or rule dependencies. Backup extraction retains the existing traversal, size-limit and rollback protections. `SelectedProfile` and `CurrentComponentConfig` in the imported Settings are used when loading data.

The GUID-keyed `AttachedObjects` on upstream attachable profile models can be read and edited without discarding unknown settings. Registered rule, notification and action settings still need typed adapters and execution. Legacy Subject fields outside the upstream model are preserved as unknown data rather than rewritten as 2.1 fields. The workbench and island remain partial WPF implementations, not a screen-for-screen migration of the Avalonia UI.

The earlier `ClassIslandRuntimeHost` implementation remains source-compatible for legacy callers during the transition, but it is no longer constructed by the plugin, included in package assets, or used for new features.

## Upstream baseline

- Repository: `ClassIsland/ClassIsland`
- Misha branch: `develop/v2/misha-alpha`
- Reviewed baseline: `08808615899d1a4abb8e0ef576bf1e247adde10f`

See `THIRD_PARTY_NOTICES.md` for attribution and source availability.
