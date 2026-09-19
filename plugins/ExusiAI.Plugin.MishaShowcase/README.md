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
