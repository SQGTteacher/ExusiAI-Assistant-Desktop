# File Viewer third-party notices

The File Viewer uses the following components for local, read-only compatibility. Their licenses remain with their respective copyright holders.

- **Docnet.Core 2.6.0** — MIT License. Provides the managed PDFium bridge and packaged native PDFium binaries used for on-demand PDF page rendering.
- **ScratchPad.NPOI.HWPF 2.5.7** — Apache License 2.0 lineage. Provides the legacy Word 97–2003 HWPF reader.
- **NPOI 2.5.6+ transitive dependency** — Apache License 2.0 lineage, as required by ScratchPad.NPOI.HWPF.
- **SharpZipLib 1.4.0+ transitive dependency** — MIT License, as required by ScratchPad.NPOI.HWPF.

- **LibVLCSharp.WPF 3.10.1 / LibVLCSharp** — LGPL 2.1. Managed binding and WPF native video view; https://github.com/videolan/libvlcsharp.
- **VideoLAN.LibVLC.Windows 3.0.24** — LGPL distribution of LibVLC and codecs. Native DLLs and plugin modules are distributed alongside the viewer. Preserve the package's license notices and source/relinking information; https://code.videolan.org/videolan/libvlc-nuget and https://www.videolan.org/vlc/libvlc.html.
- **LibreOffice** — optional separately installed application, not bundled here; primarily MPL 2.0. https://www.libreoffice.org/about-us/licenses/.
- Integration fixtures `sample.ppt` and `sample.doc` are gzip/base64 copies of NPOI's Apache-licensed test data `testcases/test-data/slideshow/WithComments.ppt` and `testcases/test-data/document/simple.doc`, from https://github.com/nissl-lab/npoi (Apache License 2.0). Video fixtures are generated using FFmpeg's `testsrc2` pattern (64×64, one second), encoded as H.264/MP4 and VP9/WebM; no third-party video content.

The viewer does not use Microsoft Office COM automation and does not execute document macros, scripts, OLE objects, attachments, or OLE objects. Embedded video playback is explicitly initiated by the user. LibreOffice runs in a private profile with macro security level 3 and Writer link updates disabled; it is not an OS sandbox.
