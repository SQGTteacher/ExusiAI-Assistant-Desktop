# ExusiAI 文件查看器

文件查看器采用“设置插件 + 独立附属程序”结构：

- `ExusiAI.Plugin.FileViewer` 只注册设置页，保存启动、外观、最近文件和默认缩放选项，并负责启动附属程序；
- `ExusiAI.FileViewer.Desktop.exe` 独立承载文件打开、全文搜索、最近记录、分页表格与 PPTX 演示；
- `ExusiAI.FileViewer.Core` 提供不依赖 WPF 的格式识别和安全读取 Provider。

独立进程降低大文档内存压力或查看器异常对 ExusiAI 主界面的影响。首页采用 Office 式深色启动中心，提供独立的主页/打开导航、主打开入口和最近文件列表；进入文档后切换为精简的 Microsoft 365 式命令栏、文档画布、辅助窗格与状态栏，但不复制其品牌素材或专有代码。

Viewer 支持拖放单个文件打开，并提供办公软件常用操作：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Shift+S` 另存为、`Ctrl+R` 从磁盘重新加载、`Ctrl+P` 打印或进入系统 PDF 打印、`Ctrl+F` 搜索、`F3`/`Shift+F3` 浏览搜索结果、`Ctrl+G` 转到文本行、`Ctrl+Z`/`Ctrl+Y` 撤销与重做、`Ctrl++`/`Ctrl+-`/`Ctrl+0` 缩放、`Ctrl+滚轮` 缩放、`F5` 全屏演示、`Esc` 退出演示，以及在 PPTX 中使用方向键、空格和 `PageUp`/`PageDown` 切页。顶部命令区按“文件 / 开始 / 查看”组织，避免窄窗口把全部操作挤在同一行。

当前支持 TXT、Markdown、RTF、CSV、DOC/DOCX、XLSX、PPT/PPTX、PDF 和本地视频。TXT/Markdown 提供基础编辑、原子保存、搜索、未保存更改保护及安全排版预览；XLSX 支持多工作表和分页读取，继续保持只读。

PDF 使用 PDFium 按需渲染，支持连续阅读、缩放、页码跳转和按页全文搜索。搜索只提取文字，不生成整本位图；离开视口的页面释放 WPF 位图，保留页面位置，需要时重新渲染。扫描 PDF 没有 OCR。

PPT、DOC、DOCX 的分页布局由可选本地 LibreOffice 转换为临时 PDF，再交给 PDFium 显示与搜索，可显示引擎支持的图片、表格、字号与分页。优先查找程序目录 `runtimes/libreoffice/program/soffice.exe`、已安装的 LibreOffice，以及 `EXUSIAI_LIBREOFFICE_PATH` 指定的完整路径。没有引擎时 DOC/DOCX 回退到既有受限文本预览；PPT 明确提示安装依赖。`settings.json` 的 `PreferOfficeLayout=false` 可让 Word 使用文本模式。转换采用私有临时配置、最高宏安全等级、禁止 Writer 自动更新链接、90 秒超时与取消时终止进程树，关闭文档删除临时文件。它是独立进程适配器，**不是操作系统安全沙箱**；静态分页不播放 PPT 内的动画或视频，也不保证与 Microsoft Office 像素一致。此版本不自动下载或安装 LibreOffice。

PPTX 保留原有逐页 DrawingML 预览，显示基础形状、文字、内嵌图片、裁剪和镜像；每批 16 页文本缩略导航、4 页 LRU 缓存和常见转场动画继续可用。新增内嵌视频按钮：点击后才提取当前视频（默认最多 64 MiB）并打开播放器，返回幻灯片或关闭文档后释放解码器和临时文件。不访问外链视频。复杂图表、SmartArt 和对象级动画仍未完整适配。

本地 MP4/M4V/MOV、MKV/WebM、AVI、WMV、FLV、MPEG 视频通过随程序打包的 LibVLC 解码，提供播放/暂停/停止、拖动进度与音量。实际可播放性取决于容器、编码与文件完整性，解码失败显示错误。`.xls` 尚未实现。PDF/分页 Office/视频暂不提供打印或导出；已有文本、表格及 PPTX 的提取导出继续可用。查看器不使用 Office COM，不启动文档宏、脚本或 OLE 对象。
