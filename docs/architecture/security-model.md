# Security model

Manifest paths are canonicalized and must remain inside the package root.
Package ids are lowercase and path-safe. Permissions are consent metadata, not
an operating-system sandbox; an in-process DLL can still call .NET APIs, so
untrusted packages must not be treated as safe.

Themes are data-only in Phase 1 and have no code entry point. The runtime
records failures without exposing stack traces in the user-facing shell.
Secrets are deliberately not stored in settings JSON. A future credential
feature should use Windows Credential Manager or DPAPI.
