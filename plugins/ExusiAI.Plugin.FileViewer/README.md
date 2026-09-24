# ExusiAI 文件查看器

文件查看器采用“设置插件 + 独立附属程序”结构：

- `ExusiAI.Plugin.FileViewer` 只注册设置页，保存启动、外观、最近文件和默认缩放选项，并负责启动附属程序；
- `ExusiAI.FileViewer.Desktop.exe` 独立承载文件打开、全文搜索、最近记录、分页表格与 PPTX 演示；
- `ExusiAI.FileViewer.Core` 提供不依赖 WPF 的格式识别和安全读取 Provider。

独立进程降低大文档内存压力或查看器异常对 ExusiAI 主界面的影响。界面采用 Lunar Client 式紧凑深色导航轨道和 Microsoft 365 式命令栏、文档画布、辅助窗格与状态栏，但不复制其品牌素材或专有代码。

Viewer 支持拖放单个文件打开，并提供办公软件常用操作：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Shift+S` 另存为、`Ctrl+F` 搜索、`Ctrl+Z`/`Ctrl+Y` 撤销与重做、`Ctrl++`/`Ctrl+-`/`Ctrl+0` 缩放、`Ctrl+滚轮` 缩放、`F5` 全屏演示、`Esc` 退出演示，以及在 PPTX 中使用方向键、空格和 `PageUp`/`PageDown` 切页。顶部命令区按“文件 / 开始 / 查看”组织，避免窄窗口把全部操作挤在同一行。

当前支持 `.txt`、`.md`、`.markdown`、`.csv`、`.docx`、`.xlsx` 与 `.pptx` 的安全查看，其中 TXT/Markdown 已支持基础文本编辑、保存/另存为、撤销/重做和未保存更改保护；大于 8 MiB 界面缓存的文本会自动保持只读，防止截断保存。DOCX 可保留标题、列表、段落和表格行结构；XLSX 支持多工作表切换、分页读取和当前工作表搜索。DOCX 暂不提供 Word 高保真分页；XLSX 不计算公式且不处理图表等高级对象；PPTX 暂不提供图片、图表、SmartArt、动画和媒体的高保真渲染；`.doc`、`.xls`、`.ppt`、`.rtf` 尚未实现。查看器不会执行宏、脚本、OLE、嵌入对象、外部链接或外部内容。

完整边界见 `docs/file-viewer/compatibility-matrix.md`。
