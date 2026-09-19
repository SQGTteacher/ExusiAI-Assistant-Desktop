# ClassIsland 2.2 Misha 功能移植

面向 ExusiAI Assistant Desktop 的课堂功能插件。原项目 ClassIsland 由
HelloWRC 创建并由 ClassIsland 开发团队与社区贡献者维护；本插件移植者为
SQGTteacher。

## 已实现功能面

- 今日课表、当前/下一课程、实时日期时间与倒计日；
- 课表及时间表编辑、轮换周、课程启停和临时调课入口；
- 组件启停、多行布局、主题、自动隐藏与鼠标穿透；
- 普通/强调提醒以及课程事件和定时自动化规则；
- 天气位置、软件时间同步和设置认证选项；
- 天气、倒计日、强调提醒、自动化和 CSES 等内置模块安装管理；
- ClassIsland 2.2 原生 Profile JSON 导入导出，保留未知字段并维持 GUID 关系；
- 单一 “ClassIsland 2.2 Misha” 工作台入口，进入后按 ClassIsland Misha 的设置窗口结构在左侧细分概览、课表与时间表、组件、提醒与自动化、扩展、档案与数据、关于。

参考基线为 ClassIsland `develop/v2/misha-alpha` 分支的 2.2 Misha 早期开发版，
分支 `develop/v2/misha-alpha`。UI 信息架构以其设置窗口左侧导航 + 右侧内容区域为基线持续同步；功能移植不会停在展示页，目标仍是完整功能与 Profile 数据互通。本插件不是 ClassIsland
官方发行版；项目身份、作者、贡献者与许可证信息见 `THIRD_PARTY_NOTICES.md`。
