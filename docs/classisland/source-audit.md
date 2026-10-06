# ClassIsland 源码核对（2026-10-06）

对照用户提供的 `ClassIsland-master.zip` 与 `third_party/ClassIsland`。比较 C#、AXAML、XAML；仅统一 BOM/换行后比较，排除已注明不导入的开发目录。

源包 SHA-256：`d85c3b3d2db42aa2800f17c30685c3f491cead06d11357666507a759cdc54fee`。

共 1179 个源码/界面文件：1168 个一致，11 个修改，0 个缺失。所有已导入 AXAML/XAML 均一致。

| 修改文件 | 改动用途 |
|---|---|
| `AssemblyInfo.cs` | 编译版本标识 |
| `ClassIsland.Core/CommonDirectories.cs` | 内嵌数据目录 |
| `ClassIsland.Core/Services/GlobalStorageService.cs` | 隔离全局配置目录 |
| `ClassIsland.Desktop/Program.cs` | 同进程 Avalonia 入口与宿主命令 |
| `ClassIsland/App.axaml.cs` | 屏蔽独立启动/更新/IPC/集控/重启行为 |
| `ClassIsland/MainWindow.axaml.cs` | 内嵌托盘与窗口恢复初始化 |
| `ClassIsland/Program.cs` | 内嵌启动参数、互斥锁及诊断隔离 |
| `ClassIsland/Services/SpeechService/GPTSovitsSecrets.cs` | 构建时私钥占位条件 |
| `ClassIsland/Services/TaskBarIconService.cs` | 托盘默认不显示 |
| `ClassIsland/Services/ThemeService.cs` | 可撤销宿主主题覆盖，默认独立主题 |
| `ClassIsland/Views/CrashWindow.axaml.cs` | 内嵌崩溃窗口不退出宿主进程 |

宿主插件自有代码用于包加载、原版运行线程、按钮/托盘入口和本机数据导入。没有自写信息岛渲染器或失败后的仿制 UI 回退。实际运行由 `ClassIsland.Desktop.Program.RunEmbedded` 启动原版 `App`，再创建上游 `MainWindow`；课表、组件编辑、临时换课通过上游 URI 导航处理器进入原版窗口。

这确认源码来源和调用链，不是 Windows 视觉验收。上游布局、动画和组件代码未重写，但窗口合成、DPI、焦点、退出与通知效果仍需 Windows 实机比对。内嵌模式有意不运行独立更新器、独立托盘、IPC 服务、集控初始化及独立插件安装流程；不能把源码一致描述成所有独立应用功能均已验收。
