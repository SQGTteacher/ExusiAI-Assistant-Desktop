# ADR 0001：消除 ClassIsland 内嵌引入的程序集版本冲突

- 状态：已采纳
- 日期：2026-10-05
- 分支：`fix/classisland-immutable-conflict`
- 基线提交：`8aa63e9`

## 背景

`ExusiAI.Plugin.ClassIsland` 通过源码级内嵌方式引用 `third_party/ClassIsland`。
Release 配置下构建 `ExusiAI.sln` 会产生 **44 条 MSB3277** 警告，全部指向同一个程序集：

```
System.Collections.Immutable, Version=8.0.0.0 与 Version=9.0.0.0 之间存在冲突
已选择 System.Collections.Immutable, Version=8.0.0.0，因为它是主版本而 9.0.0.0 不是
```

受影响项目仅两个，且互为引用关系：

- `third_party/ClassIsland/platforms/ClassIsland.Platforms.Windows`
- `plugins/ExusiAI.Plugin.ClassIsland`

本仓库启用 `TreatWarningsAsErrors`，此类冲突警告构成实际构建风险。

## 根因

依赖传递链如下（经 `project.assets.json` 与 nuspec 逐级验证，非推测）：

```
AvaloniaShared.props
  └─ PackageReference HotAvalonia 3.0.2
       └─ HotAvalonia.Core 3.0.2
            └─ System.Reflection.Metadata >= 9.0.3
                 └─ [net8.0 目标组] System.Collections.Immutable 9.0.3  → 程序集 9.0.0.0
```

而 `net8.0` 共享框架**自带** `System.Collections.Immutable` **8.0.0.0**。
两者同时进入编译期引用集，触发 MSB3277。

两个排除项（避免后续重复排查）：

- `Microsoft.Win32.SystemEvents 9.0.7` 曾被怀疑为源头，但其 nuspec 对 net8.0 目标
  **不声明** Immutable 依赖，不是成因。
- `System.Reflection.Metadata 9.0.3` 的 **net9.0** 目标组不依赖 Immutable；
  冲突仅因其 net8.0 组声明了 `System.Collections.Immutable 9.0.3`。

## 决策

在 `third_party/ClassIsland/AvaloniaShared.props` 中显式将
`System.Collections.Immutable` 钉到框架版本，并确认该降级：

```xml
<ItemGroup>
    <PackageReference Include="HotAvalonia" Version="3.0.2" PrivateAssets="All" Publish="True"/>
    <PackageReference Include="System.Collections.Immutable" Version="8.0.0" />
</ItemGroup>
<PropertyGroup>
    <NoWarn>$(NoWarn);NU1605</NoWarn>
</PropertyGroup>
```

## 曾评估但否决的方案

| 方案 | 否决原因 |
|---|---|
| 将 `HotAvalonia` 的引用条件化为仅 Debug | **编译失败**。`ClassIsland.Core/Helpers/UI/SelectorHelpers.cs:45` 在生产代码路径调用 `AvaloniaRuntimeXamlLoader.Load()`，该 API 由 HotAvalonia 提供，Release 亦需引用。 |
| 将 `System.Reflection.Metadata` 降级到 8.x | 触发 NU1605：`HotAvalonia.Core` 要求 `>= 9.0.3`。且无必要——9.0.3 本身不是冲突方。 |
| 用 `NoWarn` 屏蔽 MSB3277 | 只压制症状，9.0.0.0 仍留在闭包中，运行时加载哪个副本不确定。 |

## 理由

1. **与框架对齐**：net8.0 运行时本就提供 8.0.0.0，钉到该版本使依赖闭包与框架一致。
2. **不削弱上游约束**：`System.Reflection.Metadata` 保持 9.0.3，未触碰 HotAvalonia 的要求。
3. **移除副本而非遮蔽**：验证确认输出目录**不再复制** `System.Collections.Immutable.dll`，
   即运行时直接使用共享框架程序集，而非某一份冲突副本。
4. **影响面最小**：仅改动一个 props 文件，未触碰 ClassIsland 业务代码。

## 影响

- Release 与 Debug 构建的 MSB3277 均归零。
- 输出目录不再携带 `System.Collections.Immutable.dll`（改用框架副本）。
- 引入一处**有意的** NU1605 降级警告，已就地注明并收窄 `NoWarn` 范围。
- **技术债**：TFM 升级到 net9.0 时应移除此钉子（届时框架自带 9.0.0.0，冲突自然消失）。

## 验证结果

| 项目 | 修复前 | 修复后 |
|---|---|---|
| Release 构建 | 0 错误，404 警告，**MSB3277 ×44** | **0 错误**，382 警告，**MSB3277 ×0** |
| Debug 构建 | — | **0 错误**，388 警告，**MSB3277 ×0** |
| 单元测试 | 59 通过 / 0 失败 | **59 通过 / 0 失败** |
| 输出副本 | 携带 Immutable 9.0.0.0 | **不再复制**，使用框架 8.0.0.0 |

## 未验证项

- **未做运行时启动验证**：本次仅验证编译期引用集与单元测试，
  未实际启动 ExusiAI 并加载内嵌 ClassIsland，无法确认运行时绑定是否完全无碍。
  该验证受限于 `EXUSIAI_IMPORT.md` 中记录的另一项待办（Windows 启动与视觉检查）。
