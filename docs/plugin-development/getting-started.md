# Plugin development

Reference `ExusiAI.Extension.Abstractions` and
`ExusiAI.Extension.SDK`. Derive from `ExtensionPluginBase`, keep construction
lightweight, and perform registration in `InitializeAsync`. Start background
work in `StartAsync` only when it can be cancelled and stopped.

WPF navigation extensions may additionally reference
`ExusiAI.Extension.Wpf` and implement `IWpfNavigationExtension`. The desktop
composition layer reads these contributions only after the plugin reaches the
`Running` state. Core lifecycle contracts therefore remain independent from
WPF. Do not retain static host objects or subscribe to events without removing
subscriptions in `StopAsync`.
