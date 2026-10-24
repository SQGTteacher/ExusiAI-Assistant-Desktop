# Plugin development

Reference `ExusiAI.Extension.Abstractions` and
`ExusiAI.Extension.SDK`. Derive from `ExtensionPluginBase`, keep construction
lightweight, and perform registration in `InitializeAsync`. Start background
work in `StartAsync` only when it can be cancelled and stopped.

Plugins that own a process-scoped runtime which cannot be initialized twice may
implement `ISuspendablePlugin`. Disable then calls `SuspendAsync`, removes the
plugin's contributions, and retains its initialized instance and load context;
reenable calls `ResumeAsync`. `StopAsync` is still required to perform terminal
cleanup when the plugin is removed or the host exits. Use this contract only
when the dependency has a verified process-lifetime constraint, because a
suspended plugin is intentionally not unloadable.

WPF navigation extensions may additionally reference
`ExusiAI.Extension.Wpf` and implement `IWpfNavigationExtension`. The desktop
composition layer reads these contributions only after the plugin reaches the
`Running` state. Core lifecycle contracts therefore remain independent from
WPF. Do not retain static host objects or subscribe to events without removing
subscriptions in `StopAsync`.
