# Local ClassIsland runtime seed

For offline/local packaging, place the two files supplied for the ExusiAI integration here without renaming them:

- `ClassIsland.exe`
- `app-2.1.0.1-0.zip`

Expected SHA-256:

- `ClassIsland.exe`: `06e69ff08538c3f2c1e650914d3edfd3960f7fdda887e2d654551861f14757a8`
- `app-2.1.0.1-0.zip`: `d0bb33c1e1b79edc147b75f4a79d6c3acc7b6a6965f1b45ee7854626ea351c94`

`eng/Prepare-ClassIslandRuntime.ps1` expands these into the exact folder layout consumed by the upstream launcher. The files are build inputs only; end users do not download the runtime at first launch.

When these local seed files are absent (for example on GitHub Actions), the build script can fetch the immutable official ClassIsland 2.1.0.1 Windows x64 full-folder release and validates its fixed SHA-256 before packaging it into ExusiAI.
