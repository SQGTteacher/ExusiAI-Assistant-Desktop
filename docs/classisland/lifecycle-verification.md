# ClassIsland 生命周期验证

`ExusiAI.Desktop.exe --verify-classisland <输出目录>` 在真实 Windows WPF 进程中验证打包后的原版 Avalonia 窗口。请使用新的输出目录；探针使用其中的隔离数据，不替换用户的 ClassIsland 配置。

除原有启动、真实窗口渲染、插件安装与程序集隔离检查外，探针还会：

1. 停止首次运行，确认原版线程结束、信息岛与设置窗口销毁、内嵌应用引用释放。
2. 通过 `ExtensionRuntime` 启用尚未打开 GUI 的插件，再停用，检查页面和托盘贡献移除。
3. 连续三轮执行重新启用、托盘显示、原生窗口捕获、隐藏、打开设置、停用。每轮验证真实窗口像素、隐藏状态、旧窗口销毁和贡献移除。
4. 修改隔离数据中的原版启动画面偏好，检查停用时保存、下一轮启动时读回。内嵌模式不显示启动画面，因此这个标记不影响窗口验证。

探针使用真实插件管理器和 WPF 贡献协调器。在插件进入 Initialized、尚未 StartAsync 时，通过反射替换为使用隔离目录的宿主。后续启用、停用和卸载仍走生产代码路径。输出包括 `lifecycle.log`、每轮窗口 PNG 和最终 `result.txt`；任一检查失败则进程以非零状态退出。

CI 在框架依赖构建和 Windows x64 自包含发布中都运行该探针，单次进程总超时为 180 秒。

首次执行发现：停止第一个原版实例后，再启用会报 `Failed to create system events window thread`。SystemEvents 9 的窗口类名称仅包含 AppDomain 标识，每个插件私有加载上下文各自创建一份时会重复注册同名窗口类。修复将此依赖交由共享 WPF 契约程序集中的进程级提供器加载，保留与 WPF 自带旧版本的隔离，后续插件实例复用同一程序集。Windows 平台服务在停止时取消 TimeChanged 订阅；其他原版依赖仍按原方案隔离。回归另外断言各轮 SystemEvents 9 的程序集引用相同。

第二次 Windows 执行确认 SystemEvents 修复有效，第一轮重新启用完整渲染；第二轮重新初始化 Avalonia 时，ANGLE 的 EGL 窗口表面返回 `EGL_BAD_ALLOC`，窗口存在但捕获结果全透明。保留 Avalonia 运行时并只隐藏窗口虽避开了重新初始化，却会让通知、语音等托管服务在插件停用后继续运行，并在第二轮恢复时卡住。最终方案保留标准插件停止语义，仅让内嵌模式固定使用 Avalonia 软件 Skia 后端；原版 AXAML、控件、透明窗口和动画逻辑不变，停用时仍停止完整服务图并销毁窗口。

根因可对照 [.NET 9.0.7 SystemEvents 源码](https://github.com/dotnet/runtime/blob/v9.0.7/src/libraries/Microsoft.Win32.SystemEvents/src/Microsoft/Win32/SystemEvents.cs) 中的 `Initialize` 和 `EnsureSystemEvents`；本地并发测试加载同版本的 Windows 实现 DLL，仅检查加载身份，不在 Linux 上调用 Windows 事件 API。

这些是有限次数的生命周期回归，不代表长期运行内存、软件渲染性能、全部原版动画、DPI、通知和焦点行为已经验收。原版运行上下文目前不可回收；探针不以“窗口已关闭”推断所有程序集内存已释放。任何启停或渲染策略调整仍需结合 Windows 结果和具体资源行为。
