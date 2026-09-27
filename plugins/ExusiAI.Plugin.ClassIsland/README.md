# ExusiAI ClassIsland integration

This built-in plugin intentionally does **not** reimplement ClassIsland in WPF.
It ships and runs the upstream ClassIsland 2.1.0.1 Windows folder package as a native child runtime owned by the ExusiAI lifecycle.

## Runtime layout

The packaged plugin contains:

- `Runtime/ClassIsland.exe`
- `Runtime/app-2.1.0.1-0/**`
- `Runtime/data/**` (created by ClassIsland at run time)

The structure is the same layout expected by the upstream ClassIsland launcher. The launcher selects `app-2.1.0.1-0`, sets `ClassIsland_PackageRoot`, and the upstream `folder` packaging mode uses `<Runtime>/data` for `Settings.json`, `Profiles`, `Config`, logs, cache and other native data.

ExusiAI starts the original `ClassIsland.exe` inside a Windows Job Object. Its `ClassIsland.Desktop.exe` child inherits that job, so disabling the plugin or exiting the host cannot leave a detached ClassIsland runtime behind.

## Sync

The one retained part of the previous ExusiAI implementation is the ClassIsland backup sync path. A ClassIsland 2.x backup ZIP is validated against traversal and size limits, then its native `Settings.json`, `Profiles/**` and `Config/**` are applied directly to the bundled runtime's `data` directory. Existing unrelated `data` content is preserved. If synchronization fails, the managed entries are restored from a rollback copy.

## Upstream baselines

- Bundled runtime: ClassIsland `2.1.0.1`
- Release source commit: `15273f82c9d2d55929df83b5fb806e68ee4547c0`
- Future migration source: `develop/v2/misha-alpha`
- Misha baseline reviewed for this rewrite: `08808615899d1a4abb8e0ef576bf1e247adde10f`

See `THIRD_PARTY_NOTICES.md` for attribution and source availability.
