# ArkPets 桌宠

这是 ExusiAI 的独立桌宠插件，不属于 ClassIsland 2.2 Misha 插件。

首阶段保持 ArkPets v3.x 的使用方式和界面结构：

- 左侧仍为“模型 / 行为 / 选项”，底部保留“启动”；
- 直接读取 Ark-Models 的 `models_data.json`，兼容干员基建小人、动态立绘、时装和敌人模型；
- 生成 ArkPets 兼容配置并使用 ArkPets 的 `--direct-start --config` 入口启动桌宠；
- 行为、物理、显示、渲染和窗口选项沿用 ArkPets 的字段含义；
- 插件与 Misha 完全独立，通过轻量状态桥接读取课程阶段，不形成程序集硬依赖；可选提供上下课提醒和课间桌面整理。

## 运行时和模型

ArkPets 程序代码基于 GPL-3.0。模型资源不随本插件重新声明为 GPL，也不直接复制进 ExusiAI 仓库。
用户可以选择现有 ArkPets v3.x 程序（`ArkPets.exe` 或 `.jar`）和 Ark-Models 模型库目录。

Ark-Models 的资源版权归上海鹰角网络有限公司所有，原仓库声明不得用于商业用途、不得损害版权方利益。课间桌面整理默认关闭，只处理本节课开始后新增/修改的常见课件、文档和图片，并写入整理记录。
