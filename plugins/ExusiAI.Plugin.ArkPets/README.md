# ArkPets 桌宠

这是 ExusiAI 的独立桌宠插件，不属于 ClassIsland 2.2 Misha 插件。

首阶段保持 ArkPets v3.x 的使用方式和界面结构：

- 左侧仍为“模型 / 行为 / 选项”，底部保留“启动”；
- 直接读取 Ark-Models 的 `models_data.json`，兼容干员基建小人、动态立绘、时装和敌人模型；
- 模型页支持 ArkPets 式收藏筛选、随机选择、资源校验，以及 Ark-Models ZIP 导入/导出；
- 支持同时启动多个桌宠实例，并由 ExusiAI 内置 ArkPets IPC 主控实时切换手动模式、透明模式、形态和退出；
- 可由 ExusiAI 一键下载/更新 ArkPets 官方便携运行核心与 Ark-Models，无需单独安装 ArkPets 启动器；
- 生成 ArkPets 兼容配置并使用上游 `--direct-start --config` 运行核心启动桌宠；
- 行为、初始落点、方向切换、物理、过渡、显示、渲染、描边、透明度和窗口选项沿用 ArkPets 的字段含义；
- 插件与 Misha 完全独立，通过轻量状态桥接读取课程阶段，不形成程序集硬依赖；可选提供上下课提醒和课间桌面整理。

## 运行时和模型

ArkPets 程序代码基于 GPL-3.0。插件既支持选择现有 ArkPets v3.x 便携核心，也支持从上游 GitHub Release 下载官方 ZIP 到 `%LocalAppData%\ExusiAI\arkpets\runtime`。该运行核心由 ExusiAI 作为受管子进程启动、控制和回收，不注册 ArkPets 自身开机启动项，也不作为独立常驻软件使用。

“随 ExusiAI 启动桌宠”只在 ExusiAI 已经启动并加载插件后触发；“Windows 登录时启动”写入的目标始终是 ExusiAI 主程序。插件启动的桌宠会加入 Windows Job Object：正常退出、禁用/卸载插件，甚至 ExusiAI 异常退出时，受管 ArkPets 子进程都会随宿主结束。若检测到另一个 ArkPets Launcher/Host 正占用兼容 IPC，插件会拒绝启动桌宠，避免角色脱离 ExusiAI 管理。

Ark-Models 的资源版权归上海鹰角网络有限公司所有，原仓库声明不得用于商业用途、不得损害版权方利益。模型库可以由用户导入，也可以由插件从 Ark-Models 上游下载到 ExusiAI 数据目录；模型素材不重新声明为 GPL。课间桌面整理默认关闭，只处理本节课开始后新增/修改的常见课件、文档和图片，并写入整理记录。
