# File Viewer third-party notices

The File Viewer uses the following components for local, read-only compatibility. Their licenses remain with their respective copyright holders.

- **Docnet.Core 2.6.0** — MIT License. Provides the managed PDFium bridge and packaged native PDFium binaries used for on-demand PDF page rendering.
- **ScratchPad.NPOI.HWPF 2.5.7** — Apache License 2.0 lineage. Provides the legacy Word 97–2003 HWPF reader.
- **NPOI 2.5.6+ transitive dependency** — Apache License 2.0 lineage, as required by ScratchPad.NPOI.HWPF.
- **SharpZipLib 1.4.0+ transitive dependency** — MIT License, as required by ScratchPad.NPOI.HWPF.
- **b2xtranslator 1.0.2 and PPT source mappings** — BSD 3-Clause. Binary PPT conversion runs inside the viewer via compiled C# libraries. Source provenance: `third_party/b2xtranslator/UPSTREAM.md`; full license distributed as `licenses/b2xtranslator-BSD-3-Clause.txt`. https://github.com/Sserebryan/b2xtranslator.

- **LibVLCSharp.WPF 3.10.1 / LibVLCSharp** — LGPL 2.1. Managed binding and WPF native video view; https://github.com/videolan/libvlcsharp.
- **VideoLAN.LibVLC.Windows 3.0.24** — LGPL distribution of LibVLC and codecs. Native DLLs and plugin modules are distributed alongside the viewer. Preserve the package's license notices and source/relinking information; https://code.videolan.org/videolan/libvlc-nuget and https://www.videolan.org/vlc/libvlc.html.
- Integration fixtures `sample.ppt` and `sample.doc` are gzip/base64 copies of NPOI's Apache-licensed test data `testcases/test-data/slideshow/WithComments.ppt` and `testcases/test-data/document/simple.doc`, from https://github.com/nissl-lab/npoi (Apache License 2.0). Video fixtures are generated using FFmpeg's `testsrc2` pattern (64×64, one second), encoded as H.264/MP4 and VP9/WebM; no third-party video content.

The viewer does not bundle or launch third-party executables. Parsing and decoding dependencies run in-process as managed libraries or native DLLs. The viewer does not use Microsoft Office COM automation and does not execute document macros, scripts, OLE objects or attachments. Embedded video playback is explicitly initiated by the user.
