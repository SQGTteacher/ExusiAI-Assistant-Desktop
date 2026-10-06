# ADR 0004: Align the embedded ClassIsland theme with the shell

## Status

Accepted for phase two.

## Context

ExusiAI renders its shell with WPF. The embedded ClassIsland renders with Avalonia
(11.3.17). Both run in one process, each on its own compositor: WPF composes through
MIL/DirectX, Avalonia through Skia/ANGLE. There is no supported way to place an Avalonia
visual into the WPF visual tree as an ordinary element, so the two surfaces cannot share
a visual tree.

The island (`ClassIsland.MainWindow`) additionally depends on window-level features that a
child control cannot have. It is click-through and non-activating:

```
WindowPlatformService.cs:130-150
  WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE + SetLayeredWindowAttributes
```

It also sets `WS_EX_TOOLWINDOW`, drives topmost/bottommost with `SetWindowPos`, and applies
`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. It is a desktop overlay by design:
it must stay visible over the desktop even when the shell is hidden to the tray. Turning it
into an embedded control would remove every one of those behaviours.

## Decision

Keep the two compositors separate and integrate at the theme boundary only.

- The shell theme is already framework-agnostic (`ExusiAI.Theme` has a `net8.0` target and
  no XAML), so it can supply the variant and accent when the user enables following.
- A new opt-in contract, `IWpfHostThemeExtension`, lets a UI extension receive the shell
  theme. `WpfExtensionCoordinator` pushes it on attach and on every theme change.
- `HostTheme` carries **only** the variant (`IsDark`) and the accent. The rest of the shell
  palette deliberately does not cross the boundary.

Flow:

```
IThemeService.Changed → WpfExtensionCoordinator.PushHostTheme
  → ClassIslandPlugin.ApplyHostTheme → ClassIslandHost.ApplyTheme
  → reflection: ClassIsland.Desktop.Program.SetEmbeddedTheme(isDark, accent)
  → ClassIsland.Services.ThemeService.SetEmbeddedHostTheme(themeMode, primary)
```

`ClassIslandHost` retains the last theme and applies it on `AppStarted`, so a theme pushed
before Avalonia finishes starting is not dropped.

## Rationale

- **Upstream fidelity is the priority.** Restricting the bridge to variant and accent keeps
  FluentAvalonia's design language and the XAML theme pack untouched. Pushing the whole shell
  palette would silently re-skin ClassIsland away from upstream.
- The native bridge applies a reversible override in `ThemeService`. Upstream settings
  continue to update the independent theme underneath that override.
- **The bridge is opt-in and one-way.** Extensions that do not implement the interface are
  unaffected, and an extension cannot push theme back into the shell.
- **A failing bridge is not fatal.** Theme application is wrapped so a misbehaving extension
  cannot break the rest of the extension runtime.

## Alternatives rejected

- **Host Avalonia in a child HWND** (`EmbeddableControlRoot` + `HwndHost`). Avalonia 11.3.17 does
  ship `EmbeddableControlRoot` and `NativeControlHost`, so this is mechanically possible, but it
  requires converting ClassIsland's roughly 20 windows into embedded controls. That is a permanent
  fork of upstream, and it still cannot reproduce the island's window-level behaviours. It also
  introduces airspace limits: WPF content cannot render over or clip the hosted region.
- **Migrate the shell to Avalonia.** Would leave one framework and one theme system, but it
  invalidates the WPF extension SDK (`ExusiAI.Extension.Wpf`, `IWpfNavigationExtension`) and every
  plugin that ships against it.
- **Push the full shell palette into ClassIsland.** Rejected as part of this decision: it trades
  upstream visual fidelity for a uniformity the user did not ask for.

## Consequences

- Independent upstream theming is the default. The plugin page has a persisted follow-shell
  option, separate from imported Settings.json. Disabling it immediately restores the latest
  upstream variant and accent.
- While following, upstream settings refreshes cannot overwrite the host variant and accent.
  XAML theme packs remain under ClassIsland control.
- The island keeps running as a separate top-level window, so this decision does not address
  window chrome, positioning, or DPI coordination. Those remain open.

## Not verified

The original bridge commit was compiled and tested. This follow-up adds persistence tests,
but the current Linux environment has no .NET SDK and cannot execute Windows tests.
Windows CI must validate compilation/tests; visual switching still needs a Windows desktop.
