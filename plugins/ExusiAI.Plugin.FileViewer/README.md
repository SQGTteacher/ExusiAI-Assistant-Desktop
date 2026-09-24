# ExusiAI 文件查看器

文件查看器采用“设置插件 + 独立附属程序”结构：

- `ExusiAI.Plugin.FileViewer` 只注册设置页，保存启动、外观、最近文件和默认缩放选项，并负责启动附属程序；
- `ExusiAI.FileViewer.Desktop.exe` 独立承载文件打开、全文搜索、最近记录、分页表格与 PPTX 演示；
- `ExusiAI.FileViewer.Core` 提供不依赖 WPF 的格式识别和安全读取 Provider。

独立进程降低大文档内存压力或查看器异常对 ExusiAI 主界面的影响。界面采用 Lunar Client 式紧凑深色导航轨道和 Microsoft 365 式命令栏、文档画布、辅助窗格与状态栏，但不复制其品牌素材或专有代码。

Viewer 支持拖放单个文件打开，并提供办公软件常用操作：`Ctrl+O` 打开、`Ctrl+F` 搜索、`Ctrl++`/`Ctrl+-`/`Ctrl+0` 缩放、`Ctrl+滚轮` 缩放、`F5` 全屏演示、`Esc` 退出演示，以及在 PPTX 中使用方向键、空格和 `PageUp`/`PageDown` 切页。无文件时显示轻量启动页和最近文档，不会预加载文档 Provider。

当前支持 `.txt`、`.md`、`.markdown`、`.csv`、`.docx`、`.xlsx` 与 `.pptx` 的安全只读预览。DOCX 暂不提供 Word 高保真分页；XLSX 不计算公式且不处理图表等高级对象；PPTX 暂不提供图片、图表、SmartArt、动画和媒体的高保真渲染；`.doc`、`.xls`、`.ppt`、`.rtf` 尚未实现。查看器不会执行宏、脚本、OLE、嵌入对象、外部链接或外部内容。

完整边界见 `docs/file-viewer/compatibility-matrix.md`。
