# Phase 2 goals

Phase 2 turns the verified platform foundation into a usable local extension
desktop. Work is split into reviewable increments instead of adding classroom
features directly to the host.

## Delivered through 0.2.0-preview.3

- Native Windows 11 Mica and Acrylic backdrops with DWM-owned window corners.
- Eight persistent editor-style color schemes, including system-following mode.
- A fixed host navigation model and a dedicated single-page plugin workspace.
- Runtime plugin enable, disable, retry, unload, and persistent disabled state.
- Lock-free managed plugin loading so package files can be replaced safely.
- Discovery across bundled and per-user package directories.
- A searchable local resource catalog with package compatibility metadata.
- Runtime, build, architecture, data-directory, package-directory, and log diagnostics.
- Build-number stamping and automatic sample-package inclusion in publish output.
- A ClassIsland Misha feature port with seven navigation areas, persistent
  schedule state, clock, countdown, reminders, compact mode, theme resources,
  local data import/export, and a documented lifecycle boundary.
- Crash containment for host and plugin pages, global UI/background exception
  reporting, copyable diagnostics, and timestamped crash logs.
- Developer-oriented crash summaries, full-log access, direct repository Issue
  submission, and a structured GitHub Issue form with privacy safeguards.
- The ClassIsland Misha port now exposes seven functional areas: information
  display, schedule/timetable editing, component layout, reminders/automation,
  built-in modules, profile data, and complete upstream attribution.

## Next increments

1. A transactional local package installer with staging, validation, rollback,
   and explicit conflict reporting.
2. Package update and removal flows that preserve per-plugin settings.
3. Evolve the classroom showcase into data-provider-backed schedules and
   notifications while keeping it outside the desktop host.
4. Keyboard navigation, accessibility names, high-contrast validation, and a
   Windows visual smoke-test checklist.
5. Signed update metadata and an online catalog only after the local installer
   and trust model are complete.

The WPF shell remains replaceable. Theme definitions, package discovery,
settings, catalog logic, and extension lifecycle code do not depend on the
desktop view implementation; only `ExusiAI.Desktop` and `ExusiAI.Extension.Wpf`
would be replaced if a later visual review requires an Avalonia migration.
