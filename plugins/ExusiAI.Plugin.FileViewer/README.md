# ExusiAI 文件查看器

阶段 1 提供 `.txt`、`.md`、`.markdown` 和 `.csv` 的安全只读预览。阶段 2A 新增 `.docx` 正文结构化文本预览，并建立 Open XML ZIP/XML 安全边界。文本按 64 KiB 增量解码，界面缓存限制为 8 MiB；CSV 按 256 行分页，列表启用回收式虚拟化。所有读取均可取消且不在 UI 线程执行文件 I/O。

DOCX 当前不提供 Word 高保真分页；DOC、XLS/XLSX、PPT/PPTX、RTF、宏、OLE/嵌入对象、外部链接、图表、公式、批注尚未实现。插件不会执行宏、脚本或外部内容，也不会声称对这些能力提供兼容。完整边界见 `docs/file-viewer/compatibility-matrix.md`。
