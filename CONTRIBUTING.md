# 贡献指南

感谢你愿意改进 ExusiAI Assistant Desktop。项目以课堂环境中的稳定性、可恢复性和扩展边界为优先目标。

## 许可证约定

向本仓库提交代码、文档或资源，即表示你确认：

- 你有权提交这些内容；
- 你的贡献按本仓库的 **GNU GPL v3.0 only（SPDX: GPL-3.0-only）** 许可证发布；
- 贡献不包含来源不明、无权再许可或许可证不兼容的代码与资源；
- 移植第三方项目时会保留原作者、项目地址、许可证、修改说明和必要许可证文本。

ClassIsland 相关实现必须继续保留 HelloWRC、ClassIsland 开发团队及社区贡献者的署名。SQGTteacher 仅标注为 ExusiAI 插件移植者，不得暗示该插件是 ClassIsland 官方版本。

## 开始之前

1. 搜索现有 Issue 和 Pull Request，避免重复工作。
2. Bug 修复应附复现步骤、关键日志和对应测试。
3. 新功能优先通过插件接口实现；改变核心扩展协议时说明兼容性影响。
4. 一个 Pull Request 只解决一个明确问题，避免混合重构、功能和格式化。

## 本地验证

需要 Windows 10/11 与 .NET 8 SDK：

```powershell
dotnet restore ExusiAI.sln
dotnet build ExusiAI.sln --configuration Release
dotnet test ExusiAI.sln --no-build --configuration Release
```

涉及 WPF 页面时，还应实际打开相关页面，确认数据绑定、布局、缩放、深浅主题、窗口最大化和异常恢复均正常。

## 代码要求

- 不吞掉异常；可恢复异常应进入日志并显示可操作的错误页面。
- 插件必须支持启用、禁用、卸载及单插件故障隔离。
- 配置格式变化时必须考虑向后兼容和导入导出。
- 不提交密钥、个人信息、构建产物或来源不明的二进制文件。
- UI 不使用无功能的占位页或占位图。
- 增加 NuGet 依赖时，在 PR 中说明必要性、维护状态和许可证。

## Pull Request 内容

PR 描述至少包含修改目的、用户可见变化、测试结果、兼容性影响、第三方来源及相关 Issue（如 `Fixes #123`）。提交后须等待 Windows CI 通过。

## 安全问题

不要公开提交包含密钥、账号、学生资料或其他敏感信息的 Issue。请先清理日志，再提供最小化复现材料。
