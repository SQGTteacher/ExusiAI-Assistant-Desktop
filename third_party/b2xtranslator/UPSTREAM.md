# Binary Office translator source

PPT parser and Open XML mappings imported from https://github.com/Sserebryan/b2xtranslator
at commit `978776b9a5328af6e113eded81cee96fcb78f1f8`.
The Common library is pinned to the same maintainer's `b2xtranslator` NuGet 1.0.2.
BSD-3-Clause license is preserved in LICENSE.

Only library code is compiled into ExusiAI.FileViewer.BinaryOffice.dll.
No Shell projects, converter executables, installers or external processes are included.
The upstream csproj is retained as reference, not built. Our project embeds the default
theme/layout XML resources required by upstream's GetDefaultDocument implementation.

Local changes: read metroBlob ZIPs from memory instead of leaking temporary files;
disable XML resolvers and prohibit DTDs when reading themes/defaults; reject cyclic
or excessive PPT edit history; omit VBA payloads from generated previews. Three source files' Windows-1252 comments are normalized
to UTF-8. Whitespace-only normalization does not change upstream logic.
