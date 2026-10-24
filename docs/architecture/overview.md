# Architecture overview

The desktop project is the composition root. It creates the DI container,
settings, theme service, navigation registry, logging, and extension runtime,
then starts the WPF shell.

`ExusiAI.Extension.Abstractions` contains the stable package and lifecycle
contracts. `ExusiAI.Extension.Runtime` parses and validates `package.json` files
and loads code packages in collectible `AssemblyLoadContext` instances.
`ExusiAI.Extension.Wpf` adds the WPF navigation page contract without making
the core extension protocol WPF-specific.

Phase 2 adds runtime enable/disable, separate bundled and user package roots,
a searchable local catalog, a single-page plugin workspace, persistent visual
preferences, and native DWM window materials. An out-of-process host remains
possible because extensions receive capabilities, not the desktop host or its
service provider.
