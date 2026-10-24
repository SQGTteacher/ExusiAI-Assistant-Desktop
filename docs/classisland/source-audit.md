# ClassIsland 源码核对（2026-10-17）

对照用户提供的 `ClassIsland-master.zip` 与 `third_party/ClassIsland`。比较 C#、AXAML、XAML；仅统一 BOM/换行后比较，排除已注明不导入的开发目录。

源包 SHA-256：`d85c3b3d2db42aa2800f17c30685c3f491cead06d11357666507a759cdc54fee`。

后续变更（2026-10-24）：生命周期回归另修改 Windows 平台 `Services/SystemEventsService.cs`，增加可取消的系统时间事件订阅；`ClassIsland.Desktop/Program.cs` 在最终停止时清理该订阅，并提供仅隐藏全部原生窗口的宿主挂起入口。下方文件计数保留为 2026-10-17 的来源审计快照，不作为后续提交的实时差异统计。

上游共 1179 个源码/界面文件：1168 个一致，11 个修改，0 个缺失；另增加 1 个 ExusiAI 安装边界辅助文件。所有已导入 AXAML/XAML 均一致。

| 修改文件 | 改动用途 |
|---|---|
| `AssemblyInfo.cs` | 编译版本标识 |
| `ClassIsland.Core/CommonDirectories.cs` | 内嵌数据目录 |
| `ClassIsland.Core/Services/GlobalStorageService.cs` | 隔离全局配置目录 |
| `ClassIsland.Desktop/Program.cs` | 同进程 Avalonia 入口、宿主命令与兼容 WPF 的窗口合成 |
| `ClassIsland/App.axaml.cs` | 屏蔽独立启动/更新/IPC/集控/重启行为，并处理隔离目录中的待安装插件 |
| `ClassIsland/Helpers/PluginPackageInstaller.cs` | 插件包安全暂存、校验、原子替换与失败回滚 |
| `ClassIsland/MainWindow.axaml.cs` | 内嵌托盘与窗口恢复初始化 |
| `ClassIsland/Program.cs` | 内嵌启动参数、互斥锁及诊断隔离 |
| `ClassIsland/Services/SpeechService/GPTSovitsSecrets.cs` | 构建时私钥占位条件 |
| `ClassIsland/Services/TaskBarIconService.cs` | 托盘默认不显示 |
| `ClassIsland/Services/ThemeService.cs` | 可撤销宿主主题覆盖，默认独立主题 |
| `ClassIsland/Views/CrashWindow.axaml.cs` | 内嵌崩溃窗口不退出宿主进程 |

宿主插件自有代码用于包加载、原版运行线程、按钮/托盘入口和本机数据导入。没有自写信息岛渲染器或失败后的仿制 UI 回退。实际运行由 `ClassIsland.Desktop.Program.RunEmbedded` 启动原版 `App`，再创建上游 `MainWindow`；课表、组件编辑、临时换课通过上游 URI 导航处理器进入原版窗口。

这确认源码来源和调用链，不是完整的 Windows 人工视觉验收。上游布局、动画和组件代码未重写；CI 会在 WPF 进程内启动信息岛、检查重定向合成模式，并捕获真实 Win32 顶层窗口表面为 PNG、验证其包含非空像素，但 DPI、焦点及通知效果仍需 Windows 实机比对。内嵌模式有意不运行独立更新器、独立托盘、IPC 服务和集控初始化；隔离数据目录内排队的 `.cipx` 会在下次启动时安装。安装先在插件扫描根目录之外的同卷事务目录暂存并验证入口程序集，再原子替换；失败时保留旧插件与安装包供重试，并保持已有的禁用/待卸载状态。
