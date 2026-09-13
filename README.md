# ExusiAI Assistant Desktop

面向中小学课堂设备的、扩展优先的 Windows 桌面平台。ExusiAI 本体只提供桌面壳、扩展运行时、设置、主题、日志和市场边界；屏幕批注、点名、课件管理等教学能力将以独立扩展交付。

> 当前版本为 `0.2.0-preview.4` 第二阶段先行版本，不是可用于生产课堂的完整产品。

## 当前能力

- .NET 8 / C# 12 / WPF / MVVM 桌面壳；
- 统一 `package.json`，可表达 Plugin、Theme、Widget、Provider 等包类型；
- 清单严格解析、兼容性检查、重复 ID 检测和包路径逃逸防护；
- 可回收 `AssemblyLoadContext`、插件初始化/启动/停止生命周期和故障隔离；
- 与通用扩展协议分离的 WPF 导航扩展点；
- 8 套编辑器风格配色、System 自动切换、本地版本化设置和文件日志；
- Windows 11 原生 Mica / Acrylic 材质与 DWM 窗口圆角；
- 独立插件工作台，插件页面不再挤占主菜单；
- 插件可在运行时启用、禁用和重试，偏好会跨启动保留；
- 可搜索的本地资源库、双包目录发现、扩展管理页面和官方示例插件；
- 内置 ClassIsland 2.2 Misha 功能移植插件，覆盖课表/时间表、组件、提醒与自动化、内置扩展、原生 Profile JSON 迁移及完整开源署名；
- Windows GitHub Actions 构建及单元测试。

## 构建与运行

需要 Windows 10/11 和 .NET 8 SDK：

```powershell
dotnet restore ExusiAI.sln
dotnet build ExusiAI.sln --configuration Release
dotnet test ExusiAI.sln --no-build --configuration Release
dotnet run --project src/ExusiAI.Desktop
```

构建或发布 Desktop 时，基础示例包与 ClassIsland Misha 功能移植会分别复制到输出目录的 `packages/exusiai.sample` 和 `packages/exusiai.misha-showcase`。运行后它们应处于“运行中”状态，并出现在“插件工作台”的选择列表中；移植插件提供七个独立功能页面。

创建可分发的 Windows x64 目录：

```powershell
dotnet publish src/ExusiAI.Desktop/ExusiAI.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  --output artifacts/ExusiAI-win-x64
```

发布目标会自动带上两个范本插件包。分发时请保留整个 `artifacts/ExusiAI-win-x64` 目录。用户自行安装的包放在软件设置页显示的“用户插件包”目录中。

## 代码边界

| 项目 | 职责 |
|---|---|
| `ExusiAI.Extension.Abstractions` | 稳定、无 UI 依赖的包模型与生命周期协议 |
| `ExusiAI.Extension.Runtime` | 不可信清单的发现/验证、加载上下文和生命周期 |
| `ExusiAI.Extension.SDK` | 面向扩展作者的可选辅助 API |
| `ExusiAI.Extension.Wpf` | 仅供 WPF UI 扩展使用的贡献协议 |
| `ExusiAI.Theme` | 与 WPF 无关的主题选择和设计令牌 |
| `ExusiAI.Infrastructure` | 本地路径、设置、日志和 Windows 适配器 |
| `ExusiAI.Marketplace` | 可查询的本地包目录；不负责加载代码 |
| `ExusiAI.Desktop` | WPF 表现层和唯一组合根 |

设计与安全说明见 [`docs/architecture`](docs/architecture)，包规范见 [`docs/package-spec/package-manifest.md`](docs/package-spec/package-manifest.md)。

## 明确不包含

当前 Preview 不包含真实在线市场、云同步、AI/OCR、账号、遥测、自动更新或安装器。

## 开源许可证

本项目整体以 [GNU General Public License v3.0 only](LICENSE)（SPDX: `GPL-3.0-only`）发布。ClassIsland 相关移植保留其原作者、贡献者及许可证声明；详情见插件内的 `THIRD_PARTY_NOTICES.md`。参与开发前请阅读 [贡献指南](CONTRIBUTING.md)。
