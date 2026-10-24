# ClassIsland 生命周期验证

`ExusiAI.Desktop.exe --verify-classisland <输出目录>` 在真实 Windows WPF 进程中验证打包后的原版 Avalonia 窗口。请使用新的输出目录；探针使用其中的隔离数据，不替换用户的 ClassIsland 配置。

除原有启动、真实窗口渲染、插件安装与程序集隔离检查外，探针还会：

1. 停止首次运行，确认原版线程结束、信息岛与设置窗口销毁、内嵌应用引用释放。
2. 通过 `ExtensionRuntime` 启用尚未打开 GUI 的插件，再停用，检查页面和托盘贡献移除。
3. 连续三轮执行重新启用、托盘显示、原生窗口捕获、隐藏、打开设置、停用。每轮验证真实窗口像素、全部原生窗口隐藏、同一内嵌应用继续运行，以及页面和托盘贡献移除。
4. 修改隔离数据中的原版启动画面偏好，检查挂起/恢复之间的内存状态；最后停止运行时，验证原生窗口销毁、内嵌引用释放和设置落盘。内嵌模式不显示启动画面，因此这个标记不影响窗口验证。

探针使用真实插件管理器和 WPF 贡献协调器。在插件首次进入 Initialized、尚未 StartAsync 时，通过反射替换为使用隔离目录的宿主。后续挂起、恢复和最终停止仍走生产代码路径。输出包括 `lifecycle.log`、每轮窗口 PNG 和最终 `result.txt`；任一检查失败则进程以非零状态退出。

CI 在框架依赖构建和 Windows x64 自包含发布中都运行该探针，单次进程总超时为 180 秒。

首次执行发现：停止第一个原版实例后，再启用会报 `Failed to create system events window thread`。SystemEvents 9 的窗口类名称仅包含 AppDomain 标识，每个插件私有加载上下文各自创建一份时会重复注册同名窗口类。修复将此依赖交由共享 WPF 契约程序集中的进程级提供器加载，保留与 WPF 自带旧版本的隔离。Windows 平台服务在最终停止时取消 TimeChanged 订阅；其他原版依赖仍按原方案隔离。回归另外断言各轮 SystemEvents 9 的程序集引用相同。

第二次 Windows 执行确认 SystemEvents 修复有效，第一轮重新启用完整渲染；第二轮重新初始化 Avalonia 时，EGL 创建窗口表面返回 `EGL_BAD_ALLOC`，窗口存在但捕获结果全透明。窗口和线程在上一轮均已销毁，说明该原生渲染状态不能作为普通插件实例反复创建。宿主因此增加显式 `ISuspendablePlugin` 生命周期：ClassIsland 停用时隐藏全部原生窗口并保留同一 Avalonia 运行时，重新启用时恢复该实例；卸载或退出才执行最终停止。跨平台运行时测试验证实例保留、重新发现保护、卸载和退出清理，Windows 探针验证真实渲染和窗口生命周期。

根因可对照 [.NET 9.0.7 SystemEvents 源码](https://github.com/dotnet/runtime/blob/v9.0.7/src/libraries/Microsoft.Win32.SystemEvents/src/Microsoft/Win32/SystemEvents.cs) 中的 `Initialize` 和 `EnsureSystemEvents`；本地并发测试加载同版本的 Windows 实现 DLL，仅检查加载身份，不在 Linux 上调用 Windows 事件 API。

这些是有限次数的生命周期回归，不代表长期运行内存、全部原版动画、DPI、通知和焦点行为已经验收。ClassIsland 启动后，停用会隐藏窗口但保留其后台运行时，直到卸载插件或退出 ExusiAI；原版运行上下文目前不可回收。探针分别检查挂起和最终停止，不以“窗口已隐藏”推断服务或程序集已经释放。
