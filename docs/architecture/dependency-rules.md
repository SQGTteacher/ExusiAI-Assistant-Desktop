# Dependency rules

* Core and extension abstractions target `net8.0` and do not reference WPF.
* Runtime references abstractions, never the SDK.
* SDK references abstractions and is for extension authors.
* WPF extension APIs are isolated in `ExusiAI.Extension.Wpf`.
* Infrastructure and theme are host services; Desktop composes them.
* Plugins may reference abstractions, SDK, and (only for UI) WPF. They never
  reference Desktop.
* Project references must point toward lower layers and must not form cycles.
