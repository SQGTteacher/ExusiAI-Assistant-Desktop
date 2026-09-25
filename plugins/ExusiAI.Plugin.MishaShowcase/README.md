# ClassIsland 2.2 Misha 移植

面向 ExusiAI Assistant Desktop 的 ClassIsland 2.2 Misha 移植插件。原项目由
HelloWRC 创建，并由 ClassIsland 开发团队与社区贡献者维护；本插件移植者为
SQGTteacher。

## 上游基线

- 仓库：`ClassIsland/ClassIsland`
- 分支：`develop/v2/misha-alpha`
- 配置目标：保持 ClassIsland Profile / Settings / ComponentLayouts / Automations 的实际结构与 GUID 引用，不创建“示例配置”代替真实功能。

## 0.4.0：真实配置工作区

本阶段开始移除早期展示/模拟数据层：

- 插件启动时不再创建默认 `profile.json`、示例课表、示例组件或示例自动化；
- 用户显式选择现有 ClassIsland `Settings.json` 后，插件直接发现其
  `Profiles/`、`Config/ComponentLayouts/`、`Config/Automations/`；
- 也可直接打开已有 ClassIsland Profile JSON；
- Profile 编辑直接操作 `Subjects`、`TimeLayouts`、`ClassPlans`、
  `Classes`、`TimeRule`、`ScheduleItems`、`OrderedSchedules` 等原生节点；
- 保存采用原路径原子写回并生成 `.bak`；未知字段保留；
- 科目删除会检查课表、默认科目和日程引用，避免破坏 GUID 关系；
- 时间表结构变化会同步关联课表的 `Classes` 长度；
- 当前组件与自动化配置直接读取 Settings 指向的真实 JSON；文件不存在时只提示，
  不自动生成模板文件。

后续工作继续在同一移植线上补齐 ClassIsland 2.2 Misha 的组件编辑器、完整自动化
触发器/行动编辑器、通知系统、天气、集控、插件/主题管理、CSES 互操作和主信息岛运行时。

本插件不是 ClassIsland 官方发行版。完整原作者、贡献者与许可证信息见
`THIRD_PARTY_NOTICES.md`。


## 0.4.0：组件与自动化原生结构编辑

- ComponentLayouts 从整份 JSON 文本框升级为 `ComponentProfile -> Lines -> Children -> ComponentSettings` 结构化编辑；
- 内置组件使用 ClassIsland 上游真实 GUID；新增组件的 `Settings` 保持 `null`，由 ClassIsland 本体注册类型创建真正默认设置；
- 保留组件未知字段与已有原生 `Settings` 节点，可编辑行顺序、组件顺序和常用布局属性；
- Automations 从整份 JSON 文本框升级为 `Workflow -> Triggers -> Ruleset -> ActionSet -> Actions` 结构化编辑；
- 触发器和行动使用 ClassIsland 上游真实注册 ID；新增项 `Settings=null`，交由 ClassIsland 本体生成默认设置；
- ExusiAI 编辑器不会加载或执行自动化行动，仅验证与保存配置。


## 0.5.0：本地原生工作区与主信息岛运行时

- 导入 ClassIsland `Settings.json` 时，复制 `Settings.json`、`Profiles/` 与整个 `Config/` 到 ExusiAI 自有工作区；JSON、AXAML、主题包、图片、校验文件等均保持原相对路径，来源目录仍只读；
- 后续设置、Profile、ComponentLayouts、Automations 编辑只写 ExusiAI 副本；来源目录仅记录为同步来源，不直接修改；
- 单独导入 Profile 也会先复制到 ExusiAI 本地存储；“导出副本”不会切换当前编辑文件；
- 主信息岛运行时直接读取 `CurrentComponentConfig` 指向的原生 `ComponentProfile -> Lines -> Children`；
- 对照 Misha 上游内置组件实现，当前运行时接入日期、时钟、课程表、文本、倒计时、分割线、分组/堆叠/轮播/滚动容器，以及 Settings 中缓存天气信息；
- 主窗口读取 ClassIsland 的 `IsMainWindowVisible`、`WindowDockingLocation`、偏移、监视器索引、`WindowLayer`、`Scale`、`Opacity`、`RadiusX`、`IsIslandSeperated`、字体与点击设置；
- 未实现的第三方组件不会生成伪 UI 或空白模板，只跳过并写入插件日志，后续通过真实插件兼容层补齐；
- 自动化配置仍只编辑/保存，不执行 `classisland.os.run` 等外部行动。


## 0.7.0：安全 XAML 主题兼容层

- 读取 ClassIsland 原生 `Config/EnabledThemes.json`，保持启用顺序与“后加载主题覆盖先加载主题”的语义；
- 发现 `Config/Themes/` 下的目录主题和 ZIP 主题包，读取 `manifest.yml`、`Styles.axaml` 与主题内相对 `StyleInclude`；
- AXAML 只通过禁用 DTD/外部解析器的 XML 读取器转换成 ExusiAI 中间模型，不调用 Avalonia Runtime XAML Loader；
- 区分 ClassIsland JSON 颜色的 RRGGBBAA 与 Avalonia AXAML 颜色的 AARRGGBB；
- 中间模型支持主题字典 Default/Light/Dark、Color、SolidColorBrush、Linear/Radial/Conic Gradient、DrawingBrush、Dynamic/StaticResource、简单类型/类/附加属性选择器与常用 Setter；
- WPF 适配器可将安全静态样式应用到信息岛；ConicGradient 采用 WPF 可表达的近似，DrawingBrush 多层渐变保持层结构；
- `MainWindowBackgroundMaterialControl.line-background`、`Border.line-background` 与 `Border.line-background-frame` 作为 ClassIsland 主窗口背景语义别名兼容；
- `verticalSafeAreaPx` 会参与顶部信息岛布局，降低玻璃阴影/高光被窗口边界裁剪的风险；
- Binding、ControlTemplate、Transition、复杂伪类/组合选择器、外部 URI 和主题脚本不会执行；兼容层记录诊断，避免把主题兼容变成任意代码执行入口；
- “ClassIsland 主题”页可查看安全解析状态、启用/禁用主题并调整加载顺序；主题变化会刷新共享主题快照并通知信息岛重绘。
