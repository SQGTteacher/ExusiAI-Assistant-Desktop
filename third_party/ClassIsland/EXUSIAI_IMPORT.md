# ClassIsland source import

This directory is the source snapshot for the in-process ClassIsland migration.
The Windows desktop project is compiled separately using its .NET 9 SDK, then
its output is packaged by the ExusiAI ClassIsland plugin's Release build.

- Main source: user-supplied `ClassIsland-master.zip`, SHA-256
  `d85c3b3d2db42aa2800f17c30685c3f491cead06d11357666507a759cdc54fee`.
- Missing archive submodule `vendors/EdgeTtsSharp`: restored from
  `ClassIsland/EdgeTtsSharp`, branch `classisland-v2`, tree
  `3d5db1674e00c32b41dd26a370164271c8d10be3`. The archive did not
  record the submodule commit, so this is a separately identified snapshot.
- Excluded upstream development-only directories: `.github`, `.idea`, `.nuke`,
  `.vscode`, `Demos`, `build`, `doc`, `examples`, `installer`, and `tools`.

The source is GPL-3.0. Its own `LICENSE` and third-party notices remain in this
directory. The modifications for ExusiAI are intentionally limited to the
application boundary: `EmbeddedHostOptions`, `Program.AppEntry`,
`ClassIsland.Desktop.Program.RunEmbedded` and its embedded control surface
(`SetEmbeddedVisible`, `SetEmbeddedTheme`, `OpenEmbeddedSettings`,
`StopEmbedded`), `App` startup/restart, and the
directory/global-storage services. Embedded mode receives a private data root,
does not create ClassIsland's global mutex, change the host working directory or
process priority, start an updater/IPC server, or restart ExusiAI's executable.
The original Avalonia views, component implementations, services, data models,
and settings pages remain in the imported source. The native entry point is
called from the ExusiAI plugin; a Windows launch and visual check remains.
