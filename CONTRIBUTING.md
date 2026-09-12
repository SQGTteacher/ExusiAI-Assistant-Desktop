# Contributing

感谢你为 ExusiAI 提交改进。第一阶段优先保证边界稳定，而非增加教学功能。

提交前请确认：

1. 阅读 `docs/architecture/dependency-rules.md` 和相关 ADR；
2. 公共扩展协议保持小而明确，不公开 `IServiceProvider`、Desktop 对象或 WPF 类型；
3. 新的包输入按不可信数据处理，所有路径经安全解析；
4. 不在 Host 中实现教学功能，不提前接入云、AI、数据库、遥测或真实市场；
5. 执行 `dotnet build ExusiAI.sln --configuration Release`；
6. 执行 `dotnet test ExusiAI.sln --no-build --configuration Release`；
7. 文档、测试和实现保持一致，不提交 `bin`、`obj`、发布目录或本地设置。

增加 NuGet 依赖时，请在 PR 中说明标准库为何不足、包的维护状态及其是否会进入公共 SDK 的依赖图。
