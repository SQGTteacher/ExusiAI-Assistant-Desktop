# ExusiAI Assistant Desktop

面向中小学课堂设备的、扩展优先的 Windows 桌面平台。ExusiAI 本体只提供桌面壳、扩展运行时、设置、主题、日志和市场边界；屏幕批注、点名、课件管理等教学能力将以独立扩展交付。

> 当前版本为 `0.1.0` 第一阶段架构基线，不是可用于生产课堂的完整产品。

## 当前能力

- .NET 8 / C# 12 / WPF / MVVM 桌面壳；
- 统一 `package.json`，可表达 Plugin、Theme、Widget、Provider 等包类型；
- 清单严格解析、兼容性检查、重复 ID 检测和包路径逃逸防护；
- 可回收 `AssemblyLoadContext`、插件初始化/启动/停止生命周期和故障隔离；
- 与通用扩展协议分离的 WPF 导航扩展点；
- System / Light / Dark 主题、本地版本化设置和文件日志；
- 离线市场占位边界、扩展管理页面和官方示例插件；
- Windows GitHub Actions 构建及单元测试。

## 构建与运行

需要 Windows 10/11 和 .NET 8 SDK：

```powershell
dotnet restore ExusiAI.sln
dotnet build ExusiAI.sln --configuration Release
dotnet test ExusiAI.sln --no-build --configuration Release
dotnet run --project src/ExusiAI.Desktop
```

构建 Desktop 时，示例包会复制到输出目录的 `packages/exusiai.sample`。运行后它应处于“运行中”状态并注册“示例插件”页面，页面显示 `Hello from ExusiAI Plugin!`。

## 代码边界

| 项目 | 职责 |
|---|---|
| `ExusiAI.Extension.Abstractions` | 稳定、无 UI 依赖的包模型与生命周期协议 |
| `ExusiAI.Extension.Runtime` | 不可信清单的发现/验证、加载上下文和生命周期 |
| `ExusiAI.Extension.SDK` | 面向扩展作者的可选辅助 API |
| `ExusiAI.Extension.Wpf` | 仅供 WPF UI 扩展使用的贡献协议 |
| `ExusiAI.Theme` | 与 WPF 无关的主题选择和设计令牌 |
| `ExusiAI.Infrastructure` | 本地路径、设置、日志和 Windows 适配器 |
| `ExusiAI.Marketplace` | 市场领域边界；第一阶段不联网 |
| `ExusiAI.Desktop` | WPF 表现层和唯一组合根 |

设计与安全说明见 [`docs/architecture`](docs/architecture)，包规范见 [`docs/package-spec/package-manifest.md`](docs/package-spec/package-manifest.md)。

## 明确不包含

第一阶段不包含真实在线市场、云同步、AI/OCR、账号、遥测、自动更新、安装器、数据库或 ClassIsland 集成。

## License

公开发布前仍需由项目所有者确定正式开源许可证；当前 `LICENSE` 文件不授予开源许可。
