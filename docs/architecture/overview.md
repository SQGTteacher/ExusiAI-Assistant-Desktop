# Architecture overview

The desktop project is the composition root. It creates the DI container,
settings, theme service, navigation registry, logging, and extension runtime,
then starts the WPF shell.

`ExusiAI.Extension.Abstractions` contains the stable package and lifecycle
contracts. `ExusiAI.Extension.Runtime` parses and validates `package.json` files
and loads code packages in collectible `AssemblyLoadContext` instances.
`ExusiAI.Extension.Wpf` adds the WPF navigation page contract without making
the core extension protocol WPF-specific.

The optional `ISuspendablePlugin` contract is reserved for process-scoped native
platforms. Runtime disable removes all public contributions and awaits a full
managed-session suspension; reenable resumes the retained instance. Removal,
host shutdown, and failed resume still take the terminal stop/dispose path.
ClassIsland uses this boundary to retain one Avalonia platform while rebuilding
and disposing its notification, speech, audio, window, and other managed services
for every enabled session.

Phase 2 adds runtime enable/disable, separate bundled and user package roots,
a searchable local catalog, a single-page plugin workspace, persistent visual
preferences, and native DWM window materials. An out-of-process host remains
possible because extensions receive capabilities, not the desktop host or its
service provider.
