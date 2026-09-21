# 下一步计划（NEXT STEPS）

> 基线：分支 `refactor/steam-github-accelerator-only`，最新提交 `55302a7`（领先 `origin/main` 3 个提交）
> 当前状态：**编译通过**（`BUILD_EXIT=0`，0 错误 3 警告）· **从未运行过** · **无 PR** · 远端 `main` 未改动
> 原始目标：只保留 Steam 与 GitHub 加速功能，移除其余全部无关模块，并做结构 / 内存 / 缺陷优化

---

## ✅ 进展更新（第二轮，已落地）

> 项目定位：**自用**。因此 D2（加速数据源）按「沿用官方 `api.steampp.net`」处理，不再单列决策。

| 编号 | 项目 | 状态 | 实测结果 |
| --- | --- | --- | --- |
| P0-1 | 数据保护 DI 缺口 | ✅ 已完成 | 恢复 `LocalDataProtectionProvider`；新增 `EmbeddedAesDataProtectionProvider`（`Aes => null`，与原「非官方包」路径等价）；`Startup2` 重新调用 `AddSecurityService<...>`。**并新增回归测试锁定该注册链** |
| P0-2 | 首次真实运行验证 | ⬜ 未做 | 仍需要你用管理员权限跑一次（本地代理 / 证书 / hosts） |
| P0-3 | 测试工程 | ✅ 已完成 | 恢复被误删的 `tests/ST.Client.UnitTest.Resources`（hosts 测试的期望值来源）→ 编译通过 |
| P1-4 | 开 PR / 合并 | ⬜ 待你决定 | 远端仍无 PR，`main` 未改动 |
| P1-5 | `nuget.config` 失效源 | ✅ 已完成 | 移除 `AvaloniaCI`（HTTP 521）+ `<clear/>` + 显式 nuget.org。**已在不覆盖源的情况下完成 restore 验证** |
| P1-6 | 子模块指针 | ✅ 已完成 | 根因是 Rx.NET 自带 UWP 测试包超出 MAX_PATH；需给**子模块自身**设 `core.longpaths`（父仓库设置不继承）。子模块 dirty 数已归零 |
| P1-7 | 一键构建脚本 | ✅ 已完成 | `tools/build.ps1`（免管理员装 SDK / 子模块深度 / 长路径 / `--disable-parallel` / 可选跑测试） |
| P2-8 | 全量构建 | ✅ 已完成 | **25/25 工程全部通过，0 失败**；单元测试 **13/13 通过** |
| P2-9 | UI 运行时检查 | ⬜ 未做 | 依赖 P0-2 |
| P2-10 | 打包链路 | ⬜ 未做 | `packaging/*.sh`、`resources/ProjectPathUtil.cs` 仍引用已删工程 |
| D3 | TLS 校验放宽 | ⬜ 待你决定 | 见下文 |
| D4 | 双列表内存 | ⬜ 待实测 | 见下文 |

### 第三轮：文档与元数据改写（已完成）

按「自用、可大幅删改」的授权，做了**文档 + 元数据 + 残留清理**（不动代码结构）：

**重写**
| 文件 | 处理 |
| --- | --- |
| `README.md` | 从「Steam++ 工具箱（19KB，含已删功能的完整介绍与下载渠道）」重写为「仅做 Steam/GitHub 加速的自用版」：保留功能表、已移除清单、构建与运行要求、数据源说明、**已知限制（含未验证项）**、文档索引、GPLv3 与上游署名 |
| `README.en.md` | 同上英文版 |
| `src/README.md` | 从「58 工程结构」重写为当前的 22 个 src 工程 + 3 个测试工程的职责表 |
| `packaging/build.ps1` | 删掉依赖已删工具 `ST.Tools.Publish` 与上游发布 Token 的那半；保留逐 RID `dotnet publish`（12 个 PublishProfiles 全在），新增 `-Rid` 单目标与 `-Hash` 校验 |

**删除**（上游分发链路 / 已移除平台 / 死文件）
- `download-guide.md`、`release-keylol.md`、`release-template.md` —— 上游下载与发布流程文档，自用无意义
- `packaging/build.v1.ps1` —— V1 遗留打包脚本（`source/` 已删）
- `resources/ProjectPathUtil.cs` —— **死文件**（无任何工程编译它），且常量指向已删工程
- `resources/RewardRecord.json` —— 运营/奖励记录
- `resources/screenshot-android*.png`、`screenshot_asf_*.png` —— 已移除平台（Android / ASF）的截图
- `_probe/` —— 我在早期探测写权限时遗留的垃圾目录

**保留**（有引用或仍需要）
- `resources/AppIcon/`（被 App csproj 引用）、`Areas/`、`icon/`、`MSStore_English.png`（有引用）
- `packaging/Info.plist`、`build-osx-app.sh`、`SHA256.ps1`（无已删工程引用，仍可用）

**验证**：改写后重新构建 App → **0 错误**；无任何残留引用指向被删文件。

---

### 本轮 P2-8 新发现并修复的缺陷（全部是我上一轮裁剪引入）

| 工程 | 问题 | 处理 |
| --- | --- | --- |
| `ST.Client.Linux` / `ST.Client.Mac` | 5 处 `<Compile Include>` 悬空（指向已删的 `ServiceCollectionExtensions.AddGeneralLogging.cs`、`SteamServiceImpl.cs`、`VdfHelper.cs`、`VisualStudioAppCenterSDK.cs`） | 移除悬空项 |
| 同上 | 平台实现的 `SetCurrentUser(string)` 使用了已删的 `VdfHelper`（改写 Steam `registry.vdf` 的 `AutoLoginUser`） | 方法体改为 no-op（保留签名以维持接口契约） |
| `tests/Common.UnitTest` | `<Compile Include>` 指向已删的 `Common.ClientLib.Droid` | 移除悬空项 |
| `ST.Client` | `ProjectReference` 指向被我误删的 `Common.PinyinLib.TinyPinyin` —— 它是**桌面端**拼音实现，`AddPinyin()` 由它提供 | 恢复该工程，并只移除其 `MonoAndroid11.0` 目标框架（构建需 Android 工作负载） |
| `Common.PinyinLib.PinIn` / `CFStringTransform` | 纯移动端目标（`MonoAndroid11.0` / `Xamarin.iOS10`），无人引用，本机无法构建 | 删除（并在 sln 中摘除） |

> 教训补充：`dangling.py` 只扫 `.cs/.axaml/.csproj` 的**类型名**引用，
> 扫不到 `<Compile Include>` 的**文件路径**悬空与 `ProjectReference` 悬空 —— 这次这三类问题全靠逐工程真实构建才发现。

### 方法论更正

上一轮我把 `MacPlatformServiceImpl.cs` / `LinuxPlatformServiceImpl.cs` 里的
`AutoLoginUser` 命中判为「误报（Steam 注册表，非已删类型）」。
这次编译证明：**它确实引用了已删的 `VdfHelper`，是真缺陷。**
当时只按「是否命中已删类型名」筛选，漏掉了「引用了已删**文件**里的类型」这一情形 —— 判断依据太窄。

---

## 一、优先级总览与依赖关系

```
P0-1 修运行时 DI 缺口（数据保护）──┬──> P0-2 首次真实运行验证 ──┬──> P1-4 开 PR / 合并决策 ──> P2-10 打包链路
                                  │                            │
P0-3 修测试工程编译 ───────────────┘                            └──> P2-9 UI 运行时检查
P1-5 修 nuget.config 失效源 ────> P1-7 一键构建脚本
P1-6 子模块指针归位
P2-8 全解决方案构建（与 P0-3 并行，互为补充）
P2-11 / P2-12 / P2-13 三项决策（可并行，不阻塞 P0/P1）
P3-14 文档与收尾
```

---

## P0 — 阻塞项：当前还称不上「可用」

### P0-1 修复运行时 DI 缺口：`ILocalDataProtectionProvider` 已无任何注册

- **问题**：原 `Startup2.cs:136` 有 `services.AddSecurityService<EmbeddedAesDataProtectionProvider, LocalDataProtectionProvider>()`，
  我在裁剪时删掉了它（连带 4 个 provider 实现文件）。该调用内部还会
  `TryAddSingleton<IDataProtectionProvider, EmptyDataProtectionProvider>()`
  （见 `src/Common.ClientLib/ServiceCollectionExtensions.cs:69`）。
  结果：`ILocalDataProtectionProvider`（`LocalDataProtectionProviderBase` 实现的那个服务接口）
  **在容器里没有任何实现**。而 `Startup2` 仍调用了 `services.TryAddSecureStorage()`。
- **影响**：一旦有代码解析该服务，启动即抛 DI 异常。这是**编译通不过检测的运行时回归**，也是本分支目前最可能"一跑就崩"的点。
- **预期产出**：`Startup2` 恢复一份可用的数据保护注册（推荐直接
  `services.AddSingleton<ILocalDataProtectionProvider, LocalDataProtectionProvider>()`，
  并恢复 `LocalDataProtectionProvider.cs` —— 它只依赖仍然存在的 `LocalDataProtectionProviderBase` 与
  `IPlatformService.MachineSecretKey`，不依赖已删的 `AppSettings.Aes`）。
- **验证方式**：写一个最小冒烟断言「能解析出 `ILocalDataProtectionProvider` 与 `ISecureStorage`」并在启动路径上跑通。
- **依赖**：无。**被 P0-2 依赖。**

### P0-2 首次真实运行验证（本分支从未运行过）

- **现状**：全部验证止步于编译。代理是否真能加速、证书是否被信任、UI 是否正常，**全部未验证**。
- **预期产出**：管理员权限运行一次，勾选 Steam / GitHub 加速项，确认：
  ① 代理启动成功（日志出现「运行期设置已构建：… 匹配规则 N 条」）；
  ② `https://github.com` 与 Steam 社区页可正常打开；
  ③ 托盘图标与两个 Tab（加速 / 脚本）正常显示；
  ④ 记录 `ProxyDnsCache` 的 Hits/Misses/Coalesced 以验证 DNS 缓存确实生效（这是本次内存优化的核心论据）。
- **依赖**：P0-1。

### P0-3 修复测试工程编译并跑通 hosts 测试

- **现状**：`tests/ST.Client.UnitTest` 编译失败 1 处 ——
  `HostsFileTest.cs:12` 的 `using R = System.Application.Properties.Resources;`，
  该 `Resources` 随已删除的 `tests/ST.Client.UnitTest.Resources` 工程一起消失了。
- **预期产出**：测试工程编译通过，`dotnet test` 跑通 `HostsFileTest`（hosts 解析直接关系加速正确性，值得留）。
- **依赖**：无（可与 P0-1 并行）。

---

## P1 — 交付形态：让它能被别人用起来

### P1-4 开 PR 并决定合并策略

- **现状**：远端只有分支，**没有任何 PR**；`main` 仍是原始状态。
- **预期产出**：一个 PR（或在编译 + 运行验证通过后直接合并 `main`）。
- **决策点**：见「关键决策 D1」。
- **依赖**：建议 P0-1/P0-2 通过后再合并，否则等于把一个可能启动即崩的版本推进主干。

### P1-5 修 `nuget.config` 的失效源（否则任何人 clone 下来都 restore 失败）

- **问题**：`nuget.config` 里的 `AvaloniaCI` → `https://nuget.avaloniaui.net/repository/avalonia-all/index.json`
  已返回 **HTTP 521**，会让 `restore` 直接失败（我是用 `-p:RestoreSources=...` 绕过的）。
- **预期产出**：从 `nuget.config` 移除该源、显式加入 `https://api.nuget.org/v3/index.json`。
- **依赖**：无。与 P1-7 强相关。

### P1-6 子模块指针归位

- **现状**：`git status` 显示 ` M references/reactive`（因为我执行过 `fetch --unshallow`）。
- **预期产出**：确认并恢复记录的 submodule commit，避免把一个无意的指针变更带进 PR。
- **依赖**：无。**建议在 P1-4 之前处理。**

### P1-7 固化为「一键复现构建」脚本 / 文档

- **理由**：本次踩了 3 个非显然的坑，不写下来下次还会重复：
  ① 免管理员装 SDK 6.0.101（直接解压 zip）；
  ② `--disable-parallel`（否则 NuGet 并行还原会间歇报 `Access to the path ... denied`）；
  ③ `references/reactive` 必须完整 git 历史（Nerdbank.GitVersioning），且 Windows 需要 `core.longpaths true`。
- **预期产出**：`tools/` 下一个可执行的构建脚本 + README 片段。
- **依赖**：P1-5。

---

## P2 — 完整性、质量与决策

### P2-8 全解决方案构建

- **现状**：只验证了 App 的**依赖闭包** + 1 个测试工程。`SteamToolsV2+.sln` 里还有 42 个工程项，
  其中部分（各平台工程、`Repositories.*`、`Common.UnitTest` 等）**未验证**，可能被裁剪波及。
- **预期产出**：`dotnet build SteamToolsV2+.sln` 结果与错误清单（预期会暴露若干我未覆盖的工程）。
- **依赖**：可与 P0-3 并行。

### P2-9 UI 运行时检查

- **理由**：XAML 编译通过 ≠ 运行正常。我移除了整个用户菜单面板、`Settings_Steam` 标签页、
  捐赠页、检查更新按钮、删除账号入口，布局可能出现空洞或错位；`x:CompileBindings` 未覆盖的绑定可能运行时报错。
- **预期产出**：逐页走查记录（Tab / 设置 / 关于 / 托盘右键菜单）。
- **依赖**：P0-2。

### P2-10 打包与发布链路

- **现状**：`packaging/*.sh`、`resources/ProjectPathUtil.cs`、各 `Properties/*` 仍引用
  `Steam++.dll`、已删除的 `ST.Tools.Packager` / `DesktopBridgeLink` 等。
- **预期产出**：发布脚本可用，或明确标注「打包链路已随工具工程裁剪，需重建」。
- **依赖**：P1-4（可在合并后独立推进）。

### P2-11 / P2-12 / P2-13 三项决策（详见下节 D2 / D3 / D4）

---

## P3 — 收尾

### P3-14 更新 README / CHANGELOG

- 说明本分支与上游的功能差异（现在是一个**只做加速**的版本），并链接 `REFACTOR_REPORT.md` 第 6 章遗留说明。

### P3-15 本机磁盘清理（非仓库内）

- `C:\Users\yangy\.dotnet6`（约 600MB+）、`C:\Users\yangy\dotnet-sdk-6.0.101-win-x64.zip`（236MB）、
  `.nuget-packages`（工作区旁，未进仓库）。若不再需要构建可删除；要保留则建议移到固定位置。

---

## 关键决策点（需要你拍板）

| 编号 | 决策 | 选项 | 我的建议 |
| --- | --- | --- | --- |
| **D1** | 分支如何落地 | ① 保持分支不动 ② 开 PR ③ 验证通过后直接合并 `main` | **先 P0-1/P0-2 验证，再合并**。当前是"编译通过但没跑过"，直接进 `main` 风险偏高 |
| **D2** | 加速数据源 | ① 沿用官方 `api.steampp.net` ② 自建后端并替换 `Startup.SetApiBaseUrl` | 个人自用沿用即可；**若要二次分发建议自建**（免给上游带压力、也避免 ToS 问题） |
| **D3** | `OnCertificateValidation` 放宽上游 TLS 校验（`e.IsValid = true`） | ① 保持原样 ② 收紧为按错误类型白名单 | 这是**原实现的既有取舍**，我未改动。只用官方节点可考虑收紧 |
| **D4** | `AccelerateProjectGroupDTO` 的 `Items` / `ObservableItems` 双列表 | ① 保持（内存双份） ② 收敛为单列表 | **建议先实测**（`dotnet-counters` 看各项目组占用）再改，它会牵动 Avalonia 绑定行为 |

---

## 主要阻碍

1. **"编译通过"给了虚假的完成感**：本分支 0 编译错误，却存在 P0-1 这种"一跑就崩"的 DI 缺口。
   → 阻碍在于**缺少运行链路验证能力**，而不是代码本身。
2. **`nuget.config` 失效源 + Rx.NET 需完整历史**：使"别人 clone 下来就能构建"这件事当前不成立。
3. **裁剪范围是按"功能模块"边界判断的，而部分基础设施（如数据保护）恰好被放在功能文件里**
   （`IProtectedData` 曾声明在令牌兼容层）→ 这类"横切基础设施被误删"的风险可能还有残留，P2-8 全解决方案构建是主要探测手段。

## 复盘：这次交付暴露的方法论问题

- 第一轮我以「无 SDK + `dangling.py` 静态扫描」交付，并声称剩余问题"可能仍有个别遗漏"。
  实际装好 SDK 后编译出 **40+ 处问题，其中约 15 处是我自己引入的语义错误**（最典型：误删
  `netstandard2.1` 的 `HttpContentCompat` polyfill）。静态扫描能保证引用完整性，**替代不了编译器**。
- 教训：这类大规模裁剪必须预留「装 SDK 跑一遍编译」的预算；且**结论（"已修复/已验证"）必须先回查证据**。
  本轮还发生过一次报告把 `ScriptDTO.FileName` 写成"已修复"而实际未落地的情况，已更正。
