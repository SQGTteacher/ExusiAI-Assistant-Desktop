# 开发者预发布

当前发布请求为 `.github/release.json`，只授权 `0.1.0-developer-beta`。
发布说明和包内说明必须包含「请勿使用此版本用于生产环境，仅做开发者参考。」
产品版本由 `Directory.Build.props` 和 `ApplicationInfo.Version` 保持一致；扩展兼容级别独立维护。

1. 在现有功能分支的 PR 中修改发布请求和说明，完成本地构建、测试及工作流检查。
2. PR CI 复用自包含发布验证结果，生成 Windows x64 ZIP、SHA-256、构建来源和说明供审查；PR 没有发布权限。
3. Squash 合并后，main 的 CI 再次验证同一源码，只有发布请求在该提交中发生变化时才自动打包发布。
4. 发布工作从同一次 CI 下载已验证包体，检查提交和校验值，先上传到草稿，全部上传成功才公开为 Pre-release，且不设为 Latest。

普通开发分支 push 不触发构建；后续不修改发布请求的 main 提交不会重新打包或覆盖此版本。
手动兜底为 CI 的 `workflow_dispatch` / `publish_release`（仅 main 可发布）。
失败重试只允许继续同一提交的草稿；已公开版本和已有标签不改写。
如果 main 已前进，应重跑原发布提交失败的 job，不能把不同源码覆盖到原版本。

本地可执行检查：

```text
python -m unittest discover -s .github/scripts -p "test_*.py"
node --test .github/scripts/publish_release.test.cjs
actionlint .github/workflows/ci.yml
```

发布 ZIP 排除运行探针产生的配置与报告，保留完整运行依赖、内置插件、许可证和开发者调试符号。
ClassIsland 与文件查看器的真实 Windows 验证以及第三方 EXE 排除检查均须成功后才能打包。
