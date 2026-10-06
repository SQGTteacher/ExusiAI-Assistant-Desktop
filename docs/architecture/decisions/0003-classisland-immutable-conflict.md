# ADR 0003: Pin System.Collections.Immutable to the net8.0 framework copy

## Status

Accepted for the ClassIsland source-level embed.

## Context

The embedded ClassIsland source produced 44 `MSB3277` warnings, all for one assembly:

```
System.Collections.Immutable, Version=8.0.0.0 conflicts with Version=9.0.0.0
Version 8.0.0.0 was chosen because it is the primary version and 9.0.0.0 is not
```

Only two projects were affected, and they reference each other:
`third_party/ClassIsland/platforms/ClassIsland.Platforms.Windows` and
`plugins/ExusiAI.Plugin.ClassIsland`.

The dependency path is:

```
AvaloniaShared.props
  └─ HotAvalonia 3.0.2
       └─ HotAvalonia.Core 3.0.2
            └─ System.Reflection.Metadata >= 9.0.3
                 └─ [net8.0 group] System.Collections.Immutable 9.0.3 → assembly 9.0.0.0
```

The `net8.0` shared framework already ships `System.Collections.Immutable` 8.0.0.0, so both
copies entered the compile-time reference set.

Two suspects were ruled out by reading the nuspec files and `project.assets.json`:
`Microsoft.Win32.SystemEvents` 9.0.7 declares no `System.Collections.Immutable` dependency for
its `net8.0` group, and `System.Reflection.Metadata` 9.0.3 only pulls it in through that same
`net8.0` group.

## Decision

Pin `System.Collections.Immutable` to the framework version in `AvaloniaShared.props`, and
acknowledge the resulting downgrade with a narrowed `NoWarn`:

```xml
<PackageReference Include="System.Collections.Immutable" Version="8.0.0" />
```

`System.Reflection.Metadata` stays at 9.0.3, so the `HotAvalonia` requirement is untouched.

## Rationale

- The `net8.0` runtime already provides 8.0.0.0, so the pin makes the dependency closure match the runtime instead of adding a second copy.
- The conflicting assembly is no longer copied to the output directory; the pinned version resolves to the shared framework.
- The fix removes the cause rather than hiding it. Suppressing `MSB3277` would have left 9.0.0.0 in the closure with an indeterminate runtime winner.
- Pinning to 9.0.0.0 was not an option: the projects target `net8.0-windows`.

## Alternatives rejected

- **Make the `HotAvalonia` reference Debug-only.** Fails to compile. `ClassIsland.Core/Helpers/UI/SelectorHelpers.cs:45` calls `AvaloniaRuntimeXamlLoader.Load()` in a production path, and that API is provided by `HotAvalonia`.
- **Downgrade `System.Reflection.Metadata` to 8.x.** Raises `NU1605` because `HotAvalonia.Core` requires `>= 9.0.3`, and that package is not the conflicting party.
- **Suppress `MSB3277`.** Treats the symptom only.

## Consequences

- Release and Debug builds both report zero `MSB3277` and zero errors.
- One intentional `NU1605` downgrade warning is introduced and scoped to `NU1605` only.
- The pin must be removed when the target framework moves to `net9.0`, where the framework ships 9.0.0.0 and the conflict disappears.

## Verification

| Check | Before | After |
|---|---|---|
| Release build | 0 errors, 404 warnings, `MSB3277` ×44 | 0 errors, 382 warnings, `MSB3277` ×0 |
| Debug build | — | 0 errors, `MSB3277` ×0 |
| Unit tests | 59 passed, 0 failed | 59 passed, 0 failed |
| Output copy | ships `System.Collections.Immutable` 9.0.0.0 | not copied, resolves to framework 8.0.0.0 |

Builds were run after clearing `bin`/`obj` and restoring, so the results do not depend on
incremental state.

## Not verified

Runtime startup was not exercised. Only the compile-time reference set and the unit tests were
checked, so assembly binding at run time is unconfirmed. That check is blocked by the same open
item recorded in `third_party/ClassIsland/EXUSIAI_IMPORT.md` (Windows launch and visual check).
