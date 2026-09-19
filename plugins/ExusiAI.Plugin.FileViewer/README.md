# ExusiAI 文件查看器

阶段 1 提供 `.txt`、`.md`、`.markdown` 和 `.csv` 的安全只读预览。阶段 2A 新增 `.docx` 正文结构化文本预览；阶段 2B 新增 `.xlsx` 工作表分页预览，并复用 Open XML ZIP/XML 安全边界。文本按 64 KiB 增量解码，界面缓存限制为 8 MiB；CSV/XLSX 按页加载，列表启用回收式虚拟化。所有读取均可取消。

DOCX 当前不提供 Word 高保真分页；XLSX 不计算公式，只显示文件内缓存值，并且不渲染图表、图片、批注、宏、外部链接、数据连接、条件格式或嵌入对象。DOC、XLS、PPT/PPTX、RTF 仍未实现。完整边界见 `docs/file-viewer/compatibility-matrix.md`。
