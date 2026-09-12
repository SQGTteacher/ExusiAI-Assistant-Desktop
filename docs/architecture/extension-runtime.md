# Extension runtime

Discovery enumerates package directories and reads `package.json`. Validation
checks schema, package id, semantic versions, API compatibility, host
compatibility, entry points, dependencies, permissions, and safe relative
paths. Invalid packages are recorded as failures rather than terminating the
host.

Plugin assemblies are loaded from memory streams in a collectible
`PackageLoadContext`. This keeps managed package files replaceable while a
plugin is enabled and avoids depending on garbage-collection timing after it
is disabled. Native libraries still use the platform loader and may require a
restart before replacement.
`ExusiAI.Extension.Abstractions` and `ExusiAI.Extension.Wpf` are resolved from
the default context so contract type identity is shared. The SDK remains a
private plugin dependency. Plugin load,
initialize, start, stop, and dispose boundaries are isolated and represented
by a single `PackageState` state machine.
