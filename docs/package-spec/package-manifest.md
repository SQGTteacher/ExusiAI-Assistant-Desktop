# Package manifest

Phase 1 packages use `package.json` with schema version `1`.

Required fields are `schemaVersion`, `id`, `type`, `name`, `version`,
`apiVersion`, `publisher`, and `minimumHostVersion`. Plugin packages also
require an `entryPoint` containing a relative assembly path and fully qualified
type name. Themes and data packages do not need a DLL entry point.

Example:

```json
{
  "schemaVersion": 1,
  "id": "com.example.my-plugin",
  "type": "plugin",
  "name": "Sample Plugin",
  "version": "0.1.0",
  "apiVersion": "1",
  "publisher": "ExusiAI",
  "description": "Official sample extension",
  "minimumHostVersion": "0.1.0",
  "entryPoint": {
    "assembly": "MyPlugin.dll",
    "type": "MyPlugin.Plugin"
  },
  "permissions": []
}
```
