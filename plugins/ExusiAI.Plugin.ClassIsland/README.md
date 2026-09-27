# ExusiAI ClassIsland plugin

ClassIsland 2.2 Misha is implemented as a native ExusiAI plugin. The normal execution path does not launch or download a separate ClassIsland process.

## Core services

- `ClassIslandProfileService` reads and writes the upstream `Profile` JSON shape. GUID-keyed `Subjects`, `TimeLayouts` and `ClassPlans` remain compatible, and unknown fields are retained at every modeled level.
- `ClassIslandTimetableService` resolves `TimeRule` weekly, rotating-week, date and loop rules and maps class entries to their subjects and time-layout items.
- `ClassIslandComponentService` owns component configuration under `Config/Components` without imposing a replacement schema.
- `ClassIslandNotificationService` provides the plugin-local notification channel used by imports and future timetable transitions.
- `ClassIslandCoreService` creates, starts and stops these modules as one unit under the ExusiAI plugin lifecycle.

## Data and transfer

The plugin stores writable data in `%LocalAppData%/ExusiAI/ClassIsland` and remains portable as an ordinary ExusiAI package. It can import/export individual upstream-compatible Profile JSON files and import ClassIsland 2.x automatic-backup ZIP files. Backup extraction retains the existing traversal, size-limit and rollback protections.

The earlier `ClassIslandRuntimeHost` implementation remains source-compatible for legacy callers during the transition, but it is no longer constructed by the plugin, included in package assets, or used for new features.

## Upstream baseline

- Repository: `ClassIsland/ClassIsland`
- Misha branch: `develop/v2/misha-alpha`
- Reviewed baseline: `08808615899d1a4abb8e0ef576bf1e247adde10f`

See `THIRD_PARTY_NOTICES.md` for attribution and source availability.
