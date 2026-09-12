# ADR 0001: Separate runtime lifecycle from WPF contributions

Status: Accepted

## Context

The host needs UI extensions today, but the package model and plugin lifecycle
must survive a future presentation-framework change. Putting navigation in the
general extension context would make every plugin contract UI-aware.

## Decision

`ExusiAI.Extension.Abstractions` defines only package data, logging and plugin
lifecycle. WPF plugins opt into `IWpfNavigationExtension` from
`ExusiAI.Extension.Wpf`. The desktop composition root attaches those
contributions after runtime startup and removes them before shutdown.

## Consequences

The runtime does not reference WPF. Non-UI plugins stay platform-neutral. The
desktop owns UI-thread coordination, and another presentation layer can define
its own contribution contract without changing the core protocol.
