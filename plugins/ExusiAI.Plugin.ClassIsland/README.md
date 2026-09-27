# ExusiAI ClassIsland integration

This built-in plugin intentionally does **not** reimplement ClassIsland in WPF. It ships the upstream ClassIsland 2.1.0.1 Windows folder package and runs that native code under the ExusiAI lifecycle.

## Runtime layout and data isolation

The synchronized ExusiAI plugin contains a read-only seed:

- `Runtime/ClassIsland.exe`
- `Runtime/app-2.1.0.1-0/**`

On first start the seed is verified and copied to:

- `%LocalAppData%/ExusiAI/classisland/runtime/ClassIsland.exe`
- `%LocalAppData%/ExusiAI/classisland/runtime/app-2.1.0.1-0/**`
- `%LocalAppData%/ExusiAI/classisland/runtime/data/**`

This split is intentional. Upstream `folder` packaging writes `Settings.json`, `Profiles`, `Config`, logs and cache beside the launcher under `data`. Keeping that writable directory outside ExusiAI's synchronized plugin package prevents plugin upgrades from treating native ClassIsland user data as package drift and replacing it.

ExusiAI starts the original `ClassIsland.exe` suspended, attaches it to a Windows Job Object, then resumes it. Its `ClassIsland.Desktop.exe` child inherits that job, so disabling the plugin or exiting the host cannot leave a detached ClassIsland runtime behind.

## Sync

The only feature retained from the former hand-written Misha port is ClassIsland backup synchronization. A ClassIsland 2.x backup ZIP is validated against traversal and size limits, then its native `Settings.json`, `Profiles/**` and `Config/**` are applied directly to the managed runtime's `data` directory. Existing unrelated `data` content is preserved. If synchronization fails, managed entries are restored from a rollback copy.

## Upstream baselines

- Bundled runtime: ClassIsland `2.1.0.1`
- Release source commit: `15273f82c9d2d55929df83b5fb806e68ee4547c0`
- Future migration source: `develop/v2/misha-alpha`
- Misha baseline reviewed for this rewrite: `08808615899d1a4abb8e0ef576bf1e247adde10f`

See `THIRD_PARTY_NOTICES.md` for attribution and source availability.
