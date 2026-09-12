# Architecture overview

The desktop project is the composition root. It creates the DI container,
settings, theme service, navigation registry, logging, and extension runtime,
then starts the WPF shell.

`ExusiAI.Extension.Abstractions` contains the stable package and lifecycle
contracts. `ExusiAI.Extension.Runtime` parses and validates `package.json` files
and loads code packages in collectible `AssemblyLoadContext` instances.
`ExusiAI.Extension.Wpf` adds the WPF navigation page contract without making
the core extension protocol WPF-specific.

Phase 1 uses in-process loading with explicit failure isolation. A future
out-of-process host remains possible because extensions receive capabilities,
not the desktop host or its service provider.
