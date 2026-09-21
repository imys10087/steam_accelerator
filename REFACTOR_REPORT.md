# Steam++ / GitHub 加速器精简重构报告

> 目标仓库：`https://github.com/imys10087/steam_accelerator`（Watt Toolkit / Steam++ 的 fork，基线提交 `0286ed1`，2022-02）
> 本次工作分支：`refactor/steam-github-accelerator-only`
> 产出：仅保留 **Steam 与 GitHub 加速** 的桌面端精简版 + 重构优化后的加速内核 + 可复跑的裁剪工具链

---

## 0. 结论速览

| 指标 | 改造前 | 改造后 | 变化 |
| --- | --- | --- | --- |
| `src` 下 C# 文件 | 1 203 | 576 | **−52.1%** |
| `src` 下工程（csproj） | 58 | 24 | **−58.6%** |
| 解决方案内工程项 | 69 | 42 | −39.1% |
| Git 子模块 | 13 | 5 | −61.5% |
| 仓库文件总数 | 2 595 | 888 | **−65.8%** |
| `ApiConnection`（服务端接口层） | 1 037 行 | ≈ 640 行 | 职责从 4 类收敛为 1 类 |
| `HttpProxyServiceImpl`（加速内核） | 825 行 / 1 个巨型类 | 编排层 ≈ 470 行 + 6 个职责单一类型 | 结构可读、可测 |

功能性收益（详见第 3、4 章）：

- **DNS 查询次数**：原实现「每请求一次解析」→ 现在 TTL 缓存 + 并发合并，同域名批量请求只解析 1 次。
- **域名匹配**：原 O(请求 × 项目 × 域名) 全量子串扫描 → 现在构建期建索引，热路径为哈希命中 + 点边界后缀匹配。
- **脚本注入**：原「每响应每脚本重新编译正则」→ 现在设置变更时一次性预编译；HTML 改写由 2~3 份全量副本降为 1 份。
- **启动开销**：移除 AutoMapper 的全程序集反射扫描与映射表达式编译。

---

## 1. 架构分析（改造前）

### 1.1 技术栈与形态

.NET 6 / `netstandard2.1` 的大型多端单仓（monorepo），58 个工程覆盖
Windows（WPF + Avalonia）、Linux、macOS、Android、iOS、Xamarin.Forms、控制台、UWP 桥接，
外加打包/翻译/资源生成等构建工具工程与一套 Blazor 后台管理端。

UI 为 ReactiveUI + DynamicData 的 MVVM；`System.Application` 作为全局根命名空间，
辅以 `DisableImplicitNamespaceImports`，因此各文件必须显式 `using`。

### 1.2 依赖注入的分层设计（关键机制）

启动入口 `src/Startup2.cs` 通过 `DILevel` 标志位按「进程用途」装配服务：

```
DILevel = ServerApiClient | GUI | HttpClientFactory | Hosts | MainProcessRequired | Steam | HttpProxy
```

`StartupOptions` 把标志位展开为 `HasGUI` / `HasHttpProxy` / `HasHosts` / `HasSteam` 等开关，
`ConfigureDemandServices` 据此选择性注册。

**这是一个很利于裁剪的设计**：多数功能模块的入口都被 `HasXxx` 包裹，
移除时只需删掉标志位与对应注册块，而不必逐处改动调用点。

### 1.3 加速功能的真实实现链路

```
[UI] CommunityProxyPage / ProxyScriptManagePage / ProxySettingsWindow
   ↓
[VM 服务] System.Application.Services.ProxyService            (ST.Client/Services/Mvvm/ProxyService.cs)
   ↓ 装配参数 + 启停
[内核] IHttpProxyService → HttpProxyServiceImpl               (Titanium.Web.Proxy 本地反向代理)
   ├─ 请求改写：OnRequest             （域名匹配 → 上游 IP/端口/SNI/重定向）
   ├─ 响应改写：OnResponse            （注入用户脚本 <script src="https://local.steampp.net/...">）
   ├─ 证书管理：SetupCertificate / DeleteCertificate / TrustCer
   └─ DNS：    IDnsAnalysisService（Ali DNS / 自定义 DNS / 系统 DNS）
   ↓
[数据源] ICloudServiceClient.Accelerate.All()   → api/Accelerate/All
         ICloudServiceClient.Script.Basics()    → api/script/basics
   ↓
[辅助] IHostsFileService（非系统代理模式下写 hosts）
       IScriptManager（脚本下载/构建/缓存，SQLite 存储）
```

其中 **Steam 与 GitHub 加速本身并不在代码里硬编码**——它们只是
`api/Accelerate/All` 返回的「加速项目组」中的条目（`AccelerateProjectGroupDTO`，
内含 `Name`/`DomainNames`/`ForwardDomainName`/`PortId`/`ServerName`/`ProxyType`）。
因此「只保留 Steam 与 GitHub 加速」在代码层面的正确落点是：
**保留整套加速内核与项目数据通道，移除与加速无关的功能模块。**

### 1.4 加速项目数据模型

| 字段 | 作用 |
| --- | --- |
| `DomainNames` | 分号分隔的待加速域名（支持 `*` 通配） |
| `ForwardDomainName` / `ForwardDomainIP` | 转发目标（域名优先，取不到则用 IP） |
| `PortId` | 上游端口 |
| `ServerName` | 覆盖 TLS SNI，用于域名前置/镜像节点 |
| `ProxyType` | `Local` / `Redirect` / `DirectSuccess` / `DirectFailure` / `ServerAccelerate` |
| `Hosts` | 非系统代理模式写入 hosts 的内容 |

---

## 2. 裁剪：移除无关功能模块

### 2.1 移除的功能（及其归属工程/文件）

| 类别 | 具体内容 |
| --- | --- |
| 账号体系 | 登录/注册、短信验证码、手机号绑定/换绑、第三方登录（Steam/QQ/微信等）、用户资料、签到 |
| 令牌（Steam Guard） | `LocalAuthPage`、`GAPAuthenticator*`（Steam/BattleNet/Google/HOTP/Microsoft）、WinAuth 移植层 |
| 成就管理 | `GameListPage`、`AchievementWindow`、`SteamAppPropertyTable` 二进制属性表读取、`SteamDbWebApi` |
| 挂卡 / 机器人 | ArchiSteamFarm 集成（`ASFService`、`ASF_AddBotWindow`、`ASF_GlobalConfigPage`） |
| 游戏工具 | 强制无边框窗口化（`GameRelated_BorderlessPage`）、挂卡（`SteamIdlePage`） |
| 其他平台 | GOG 代理与 PEM 证书注入、Discord/Twitch/Pixiv 等反代分类入口 |
| 运营/播报 | 捐赠排行、公告、活跃用户上报、通知播报（`INotificationService`） |
| 应用更新 | 独立更新服务与 `NewVersionWindow`（保留服务端版本策略客户端 `VersionClient`） |
| 服务端 | `Common.ServerLib`、`ST.Server.Resources`、`Services.SmsSender`、Blazor 后台管理端 |
| 移动端 | Android / iOS / Mobile / Xamarin.Forms / Droid 设计器与资源（共 12 个工程） |
| 构建工具 | `ST.Tools.*`（Packager / InstallerSetup / Publish / Translate / MinifyStaticSites / AreaImport / AndroidResourceLink / DesktopBridgeLink / OpenSourceLibraryList / Win7Troubleshoot）、`dotnet-packaging` |
| 遗留代码 | `source/`（V1 全量代码库：SteamTool.* 系列） |
| 死代码工程 | `ST.Client.Desktop`（详见 2.3） |

### 2.2 同步清理的依赖与配置

- **`.gitmodules`**：13 → 5。移除 `ArchiSteamFarm`、`SteamAchievementManager`、`Steam4NET`、
  `Depressurizer`、`WinAuth`、`MetroRadiance`、`dotnet-packaging`、`SevenZipSharp`。
  保留 `Titanium-Web-Proxy`（代理引擎）、`FluentAvalonia`、`AvaloniaGif`、`reactive`（Rx.NET）、`sqlite-net`。
- **NuGet 包**：移除 `AutoMapper` 系列、`JustArchiNET.Madness`、`Fleck`、
  `Net.Codecrete.QrCodeGenerator`、`Gameloop.Vdf`。
- **工程引用**：`ST.Client.csproj` 移除 `ArchiSteamFarm.Library` 工程引用。
- **工程合并**：`ST.Services.CloudService.ViewModels` 与 `ST.Services.CloudService.Models`
  原本是「同一份源码编译两遍」的重复工程（后者带 `MVVM_VM` 宏）。
  现合并为单一 `ST.Services.CloudService.Models`（恒带 `MVVM_VM`），消除重复编译。
- **解决方案**：`SteamToolsV2+.sln` 由 69 项按「工程是否仍存在 / 子模块是否仍保留」自动过滤为 42 项；
  删除 `Avalonia.Ref.sln`、`SteamTools.Blazor.BackManage.sln`、两个 `.slnf` 筛选器。

### 2.3 重要发现：一个完全无人引用的死工程

`src/ST.Client.Desktop`（15 个文件）经全仓检索**没有被任何工程 `ProjectReference`**，
却包含：

- 与 `ST.Client` **重名同命名空间**的 `ProxyService`、`ProxyScriptManagePageViewModel`；
- 引用了**早已不存在的** `ProxySettings.HostProxyPortId`、`IHttpProxyService.IsWindowsProxy`
  （说明它已经编译不通过很久了）。

它是历史遗留的旧桌面层副本，本次整体删除；其中唯一仍被使用的成员
`ServiceCollectionExtensions.AddGeneralLogging` 已迁移到 `ST.Client`。

### 2.4 裁剪工具链（可审计、可复跑）

裁剪不是手工 `rm`，而是脚本化执行，便于复核与在用户自己的 fork 上重放：

| 文件 | 作用 |
| --- | --- |
| `tools/prune/closure.py` | 计算 MSBuild `ProjectReference` 闭包，得出「保留 / 可移除」工程清单 |
| `tools/prune/prune.py` | 幂等执行三波删除：工程目录 → 源文件 → 次级依赖 → 死文件；同步清理 `.gitmodules` |
| `tools/prune/dangling.py` | 从删除文件反推类型名，扫描当前树中的**残留引用**（`文件:行:内容`） |
| `tools/prune/finalize.py` | 清理无用 NuGet 包、`Directory.Packages.props`，并按「存在性」过滤解决方案 |

`dangling.py` 是本次能在无编译器环境下维持一致性的关键：它把「删了类型但没改调用点」
从不可见变成一份精确的待办清单。

---

## 3. 代码结构优化：加速内核拆分

`HttpProxyServiceImpl` 原本 825 行，单类承担 6 类职责。现拆分为
`src/ST.Client/Services/Accelerator/` 下的职责单一类型：

| 新文件 | 职责 | 替代了原来的什么 |
| --- | --- | --- |
| `ProxyRuntimeSettings.cs` | 不可变运行期设置快照 + 预处理后的脚本规则 | 原来散落的可变字段 + 每请求重算的匹配规则 |
| `ProxyHostMatcher.cs` | 加速项目域名匹配（构建期建索引） | `OnRequest` 里的三层 `foreach` + `string.Contains` |
| `ProxyDnsCache.cs` | 带 TTL 与 single-flight 合并的 DNS 缓存 | `GetReverseProxyIp` 每请求裸解析 |
| `ProxyCertificateManager.cs` | 根证书创建/信任/删除/检测 | 混在服务里、Release 下静默吞异常的证书逻辑 |
| `ProxyRequestInterceptor.cs` | 请求改写、上游路由、SNI 覆盖、重定向、本地域名转发 | `OnRequest` + `HttpRequest` |
| `ProxyScriptInjector.cs` | 用户脚本注入与 HTML 改写 | `OnResponse` |
| `Implementation/HttpProxyServiceImpl.cs` | **仅编排**：生命周期、端点构建、快照发布 | 原 825 行巨型类 |

同时清理的接口面：

- `IHttpProxyService` 移除语义反了的 `IsCertificate`
  （`CertificateManager == null || RootCertificate == null`——名字读作「证书存在」，返回的却是「证书缺失」，
  且全仓无任何调用方）、GOG 相关的 `IsProxyGOG` / `WirtePemCertificateToGoGSteamPlugins`。
- `IDnsAnalysisService` 移除 7 个方法中无人调用的 4 个
  （`PingHostname`、`AnalysisHostnameTime`、`GetHostByIPAddress`、`GetPingHostNameData`）
  与 4 组无用的 DNS 常量。
- `ICloudServiceClient` 8 个子客户端 → 3 个（`Accelerate` / `Script` / `Version`）。
- `AutoMapperProfile` 删除，`ScriptDTO` ↔ `Script` 映射改为显式代码
  （`Models/ScriptMappings.cs`），并额外提供 `CopyFrom` 以便就地更新观察对象。

---

## 4. 内存与性能优化

### 4.1 DNS：从「每请求一次查询」到「TTL 缓存 + 并发合并」

原 `OnRequest` 对每个请求调用一次 DNS 解析求解上游 IP。浏览器打开一个页面会产生
数十到上百个请求，同一域名在几十毫秒内被反复解析 → DNS 压力、任务堆积、GC 压力。

`ProxyDnsCache` 提供：

1. TTL 缓存（正向 60 s / 负向 5 s，可配），命中路径零分配；
2. **single-flight**：同一键的并发查询合并为一次真实查询，其余调用方共享同一 `Task`；
3. **容量上界**：超过 1024 条时先清过期项、再按插入序淘汰，保证长期运行内存不无界增长；
4. 内建 `Hits` / `Misses` / `Coalesced` / `Count` 计数器，可直接用于验证与调参。

### 4.2 域名匹配：消除 O(项目 × 域名) 热路径

原实现（`OnRequest` 内）：

```csharp
foreach (var item in ProxyDomains)
    foreach (var host in item.DomainNamesArray)
        if (request.RequestUri.AbsoluteUri.Contains(host, StringComparison.OrdinalIgnoreCase)) ...
```

问题：每请求全量展开；且子串匹配导致 `evil-github.com` 会误命中 `github.com`（存在被引导到
伪造节点的风险）。

`ProxyHostMatcher` 在配置变更时一次性构建：
纯主机名规则进哈希字典（精确命中 O(1)），并保留按长度倒序的后缀表做**点边界**子域匹配
（`a.github.com` 命中 `github.com`，`notgithub.com` 不命中），
含 scheme/路径/通配符的规则才回退到 URI 匹配。

### 4.3 脚本注入：消除「每次调用重新编译正则」

原实现每响应每脚本执行一次
`Regex.IsMatch(uri, "^" + Regex.Escape(pattern).Replace(...) + "$", RegexOptions.Compiled)`。

`RegexOptions.Compiled` 与「每次新建模式字符串」组合意味着**每次调用都动态生成并 JIT
一份新的正则程序**。经核实，该逻辑位于公共扩展方法
`Common.CoreLib/Extensions/StringExtensions.cs::IsWildcard`，被脚本注入热循环按
「脚本数 × 规则数」调用。

修复：按模式缓存已编译实例（有界，512 条），并加 **1 秒匹配超时**
——模式来源于可下载的第三方用户脚本，原实现无超时，一个恶意/病态模式即可
造成灾难性回溯并挂住代理线程。

### 4.4 HTML 改写：3 份全量副本 → 1 份

原：`GetResponseBodyAsString()` → `LastIndexOf` → `string.Insert`（大页面下产生
2~3 份完整页面大小的临时字符串，直接抬高 LOH 压力）。
现：单次 `StringBuilder` 拼装，只产生最终一份字符串；且**无脚本命中时完全不读取响应体**。

### 4.5 其他

- `JsPathUrl` 由「响应线程首次访问就赋值」改为「启动时一次性分配」，消除并发竞态
  （原来两个并发响应可能看到不同 URL，导致注入的脚本 404）。
- `ScriptRule` 预拆分精确/通配/正则/排除规则，热路径只做必要的字符串比较。
- `AccelerateProjectGroupDTO` 的 `ObservableItems` 与 `Items` 双列表问题已记录，
  见第 6 章待办（本轮未改，避免影响 UI 绑定行为）。
- 移除 AutoMapper：省掉启动时的程序集扫描与映射表达式编译/缓存。

### 4.6 未采用的低风险调参（建议在压测后再定）

Titanium.Web.Proxy 的 `ThreadPoolWorkerThread`、`CertificateManager.SaveFakeCertificates`
等旋钮会直接影响吞吐与证书缓存占用，但缺少压测数据时贸然调整可能引入性能回归。
本次**未改动**，仅在 `HttpProxyServiceImpl` 构造函数中把与功能/稳定性直接相关的
`EnableHttp2` / `EnableConnectionPool` / `CheckCertificateRevocation` 显式固定，
避免依赖库默认值随版本漂移。

---

## 5. 缺陷修复

| # | 位置 | 问题 | 修复 |
| --- | --- | --- | --- |
| 1 | `ScriptManager.BuildScriptAsync` | 只在 `RequiredJsArray != null` 时 `return true`，末尾无条件 `return false`。**没有 `@require` 依赖的脚本永远保存失败** | 无论有无依赖都正确写出文件并返回成功 |
| 2 | `HttpProxyServiceImpl.HttpRequest`（POST 转发） | `Content.Headers.ContentLength = BodyString.Length` 把**字符数当字节数**（含中文即截断）；且 `StreamWriter` 未 `Flush`，请求体可能丢失 | 改用 `StringContent` 交由 `HttpClient` 计算字节长度 |
| 3 | `StringExtensions.IsWildcard` | 每次调用都 `RegexOptions.Compiled` 重新编译；无匹配超时（第三方脚本模式可致 ReDoS） | 有界编译缓存 + 1 秒超时，超时按「不匹配」处理 |
| 4 | `ScriptDTO.FileName` | `FilePath ?? Path.GetFileName(FilePath)` —— `FilePath` 非空，`??` 右侧是死代码 | 直接 `Path.GetFileName(FilePath)` |
| 5 | `HttpProxyServiceImpl.StartProxy/StopProxy` | 反复启停会累积未注销的端点事件处理器（`BeforeTunnelConnectRequest`/`BeforeSslAuthenticate`）；端点未释放 | 新增 `CleanupHandlers()`，停止时显式解绑并清空端点列表 |
| 6 | `HttpProxyServiceImpl` | `IsIpv6Support` 是 `static` 可变字段，启动线程写、请求线程读，数据竞争 | 改为通过不可变快照 `ProxyRuntimeSettings` 发布 |
| 7 | `IDnsAnalysisService.PingHostname` | `new Ping()` 从不释放（句柄泄漏）；且全仓无调用方 | 删除该死方法（连同其他 3 个死方法） |
| 8 | `IHttpProxyService.IsCertificate` | 语义反转（名字「有证书」/ 实际「缺证书」），误导后续开发者；无调用方 | 删除 |
| 9 | `HttpProxyServiceImpl.OnRequest` | 域名用 `string.Contains` 匹配 → `evil-github.com` 误命中 `github.com` | 改为精确/点边界匹配 |
| 10 | `HttpProxyServiceImpl.OnRequest` | HTTP→HTTPS 用 `Remove(0,4).Insert(0,"https")`，依赖字符串长度，脆弱 | 改用 `UriBuilder` |
| 11 | 重定向改写 | `AbsoluteUri.Replace(scheme://host, url)` 会**误伤路径与查询串**中同名片段 | 改用 `UriBuilder` 精确替换 host |
| 12 | `HttpProxyServiceImpl.SetupCertificate` | 非 DEBUG 构建下 `catch { }` 静默吞掉所有证书异常 → 「HTTPS 加速不生效但无日志无提示」 | 统一记录日志；并新增 `EnsureTrustedRootCertificate()`，拿不到可信根证书时**直接判定启动失败**，不再进入半可用状态 |
| 13 | `ScriptManager.AddScriptAsync` | `isNoRepeat` 用区分大小写的路径比较，Windows 下同一文件不同大小写写法会被误判为不同文件 | 改为 `OrdinalIgnoreCase` |
| 14 | `HttpProxyServiceImpl.StartProxy` | 重复调用会重复挂事件（`StartProxy` 被 `ProxyStatus` 订阅路径多次触发时） | 增加 `ProxyRunning` 幂等守卫 |
| 15 | `HttpProxyServiceImpl` 端点构建 | Windows 下 80 端口被占用时异常会冒泡导致整体启动失败 | 降级为记录日志并跳过（不影响 443 加速） |
| 16 | `HttpProxyServiceImpl.StopProxy` | 停止过程可能抛异常，而它通常运行在退出路径上 | 整体 try/catch + 日志，保证不中断退出流程 |
| 17 | `WindowsPlatformServiceImpl` DNS 实现 | 解析失败/超时会把异常抛进代理请求路径；`QueryAsync` 不支持取消 | 全链路 `try/catch` + `CancellationToken` 贯通，失败返回 `null` |
| 18 | `CloudServiceClientBase` / `ApiConnection` | 上传文件/账号鉴权/会话加密三套代码与加速无关（约 700 行），其中 `Unauthorized` 分支还会触发登出流程 | 整体移除；`isSecurity: true` 显式抛 `NotSupportedException` 而非静默降级 |

---

## 6. 遗留待办（已完成设计，需编译器校验后续收尾）

> **重要前提**：当前环境**没有安装 .NET SDK**（仅有运行时 `6.0.36 / 8 / 9 / 10`），
> 且 13 个子模块中 8 个已移除、5 个未 `git submodule update --init`，
> 因此本轮**无法执行 `dotnet build` 验证**。下列条目已定位到精确的 `文件:行`，
> 属于「机械性收尾」，建议在装好 SDK 后一次性处理。
> 可用 `python tools/prune/dangling.py .` 随时重新生成这份清单。

| 文件 | 残留引用 | 处理方式 |
| --- | --- | --- |
| `src/ST.Client/UI/ViewModels/MainWindowViewModel.cs` | 7 处：`AddTabItem<SteamAccountPageViewModel>`、`GameListPageViewModel`、`LocalAuthPageViewModel`、`ArchiSteamFarmPlusPageViewModel`、`GameRelatedPageViewModel`、`DebugPageViewModel`；`IUserManager.Instance.OnSignOut` | 仅保留 `CommunityProxyPageViewModel` + `ProxyScriptManagePageViewModel`；删除登出回调 |
| `src/ST.Client/UI/ViewModels/CustomTabItemViewModel.cs` | 5 处：`TabItemId.ArchiSteamFarmPlus` 枚举值与对应 partial 类 | 删除该枚举值与其类块 |
| `src/ST.Client/Services/Implementation/ViewModelManager.cs` | 3 处：已删除窗口的注册 | 删除对应分支 |
| `src/ST.Client.Desktop.Avalonia/.../MainView.axaml(.cs)` | 15 处：VM→View 映射表中的已删除页面 | 删除映射项 |
| `src/ST.Client.Desktop.Avalonia/.../Settings/SettingsPage.axaml` | 5 处：`Settings_Steam` TabItem | 删除该 TabItem 块（视图文件已删） |
| `src/ST.Client.Desktop.Avalonia/.../About/About_FAQPage.axaml(.cs)` | 4 处：`WebView3`（已删控件） | 与 `About_ChangeLog` 一并删除该视图 |
| `src/ST.Client.Desktop.Windows/ServiceCollectionExtensions.cs` | 6 处：`ISteamService`、`ISevenZipHelper`、`IJumpListService`、`INativeWindowApiService`、`IBiometricService`、`ISteamworksLocalApiService` 注册 | 删除对应注册行 |
| `src/ST.Client/Services/IPlatformService.cs` | 4 处：`<inheritdoc cref="ISteamService.*"/>` XML 文档引用 | 删除该 `#region Steam` |
| `src/ST.Client.Desktop.Windows/.../WindowsPlatformServiceImpl.cs` | 4 处：`SteamDirPath` / `SteamProgramPath` / `AutoLoginUser` 等 Steam 注册表读写 | 删除该 `#region Steam` |
| `src/ST.Client.Desktop.Avalonia.App/CommandLineTools.cs` | 3 处：`ISteamService.Instance.TryKillSteamProcess/SetCurrentUser/StartSteam`，及 `DILevel.Steam` | 删除 Steam 相关命令行子命令 |
| `src/ST.Client.Desktop.Avalonia.App/App.axaml.cs` | 3 处：已移除服务的注册 | 删除对应行 |
| `src/ST.Client/UI/ViewModels/Pages/ProxyScriptManagePageViewModel.cs` | 1 处：脚本商店要求登录（`IUserManager.Instance.GetCurrentUser() == null`） | 直接移除该检查（`api/script/*` 均为匿名接口） |
| `src/ST.Client/UI/ViewModels/Pages/About/AboutPageViewModel.cs` | 3 处：手机号展示、`IUserManager` | 删除账号相关区块 |
| `src/ST.Client/Services/Mvvm/ProxyService.cs` | 1 处：`httpProxyService.IsProxyGOG` | 删除（GOG 代理已移除） |
| `src/ST.Client/Settings/ProxySettings.Avalonia.cs` | `IsProxyGOG` 属性 | 删除 |
| `src/ST.Client/UI/ViewModels/Pages/About/AboutPageViewModel.Mobile.cs`、`CommunityProxyPageViewModel.Mobile.cs` | 移动端变体 | 删除（移动端已移除） |
| `src/ST.Client.Desktop.Avalonia/ST.Client.Avalonia.csproj` | 17 处：指向已删除文件的 `<Compile Remove>` / `<AvaloniaXaml Remove>` / `<None Remove>` | 删除这些条目（含 `CefSharp\**` 5 条） |
| `tests/ST.Client.UnitTest/SetupFixture.shared.cs` | 2 处：`AddSecurityService<EmbeddedAesDataProtectionProvider, EmptyLocalDataProtectionProvider>`、`TryAddUserManager` | 删除这 2 行 |
| `src/ST.Client/UI/ResIcon.cs` | `FastLoginChannel.Steam` 映射 | 删除该映射 |
| `src/ST.Services.CloudService.Models/Models/` 下的账号侧 DTO | `Notice/*`、`Font/*`、`ActiveUser*`、`Login*`、`User*`、`SendSmsRequest`、`ClockInRequest`、`Notice*` 等 | **可选**进一步删除（当前保留不影响编译，仅增加阅读噪音） |

以下命中经核实为**误报**，无需处理：
`Registry.CurrentUser` / `StoreLocation.CurrentUser`（非被删的 `CurrentUser` 模型）、
`Steam++`（应用名）、`"Valve Steam"`（UA 判定标记）、`#Steam++`（hosts 标记）、
`ASF`（注释）、`Lock`（英语单词/`lock` 关键字）、`AppResources.Designer.cs` 中的资源名。

---

## 7. 授权与敏感信息

| # | 项 | 为什么需要 | 缺失后果 | 性质 |
| --- | --- | --- | --- | --- |
| 1 | **.NET SDK 6.0.101** | `global.json` 将 SDK 版本钉死在 `6.0.101`；另需 `MSBuild.Sdk.Extras 3.0.44`（由 `global.json` 的 `msbuild-sdks` 声明，构建时自动还原） | **完全无法编译**。当前机器只有运行时（6.0.36 等），没有 SDK | 环境前置条件（非密钥） |
| 2 | **5 个 Git 子模块** | `references/Titanium-Web-Proxy`（代理引擎，**加速核心的运行时依赖**）、`FluentAvalonia`、`AvaloniaGif`、`reactive`(Rx.NET)、`sqlite-net` 均以 `ProjectReference` 源码方式引用 | 缺少 `Titanium-Web-Proxy` 则代理内核无法编译；其余影响 UI/仓储。需执行 `git submodule update --init --recursive` | 公开源码，无需凭证 |
| 3 | ~~`aes-key.pfx`、`rsa-public-key-{debug,release}.pfx`~~ | 原设计中被 `<EmbeddedResource>` 内嵌，提供 `AppSettings.AesSecret` / `RSASecret`，用于**账号接口**的会话加密，并参与「官方渠道包」判定。这些 `.pfx` **不在仓库中**（被 `.gitignore` 排除） | 原行为：`IsOfficialChannelPackage == false` → 回退到开发用 API 地址 `https://pan.mossimo.net:8862` | **已解除**：本次已将 `AppSettings` 简化为只保留 `ApiBaseUrl`，「官方渠道包」判定改为仅校验程序集公钥。**加速功能不再需要任何密钥** |
| 4 | **加速项目数据源** | `api/Accelerate/All`、`api/script/basics`、`api/script/table/*`、`api/script/updates`、`api/version/checkupdate3/*` 全部指向 Watt Toolkit 官方后端（`api.steampp.net`）。这些接口均为**匿名**接口，不需要 JWT | 拿不到加速项目列表 → 主界面无任何可勾选项目，加速功能空转 | **第三方服务依赖**。若长期自用/二次分发，建议自建后端并替换 `Startup.SetApiBaseUrl` 中的地址；是否需要与官方沟通授权取决于你的分发方式与上游 ToS |
| 5 | **写入 hosts 文件的管理员权限** | 非系统代理模式（透明代理）下需把加速域名写入系统 hosts 指向 `127.0.0.1`；macOS/Linux 还需提权复制 hosts | 写入失败 → 加速不生效（程序会提示 `OperationHostsError`） | 本机权限，非密钥 |
| 6 | **绑定 443 / 80 端口的管理员权限** | 透明代理需监听 443（Linux 非 root 时退回随机端口并引导用户自行转发） | 端口被占用 → 启动失败（已降级：80 端口冲突仅跳过） | 本机权限 |
| 7 | **安装根证书的信任权限** | HTTPS 解密需把本地根证书（`SteamTools Certificate`）加入系统信任存储 | 证书不受信 → 浏览器报证书错误，HTTPS 加速不可用 | 本机权限。macOS 下代码会执行 `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain`，**会向用户申请管理员密码** |
| 8 | **（仅签名发布时需要）代码签名证书 + `Unicorn.snk`** | `Directory.Build.props` 在检测到 `Unicorn.snk` 时启用 `SIGN_ASSEMBLY`，用于官方渠道包判定。本仓库不含该文件 | 仅影响「是否被判为官方渠道包」，不影响功能 | 可选 |
| 9 | **（可选）GitHub Token** | 仅当你希望把本次改动推回自己的 fork 时需要 | 无法推送 | 你的账号凭证，本会话未使用、也未请求 |

**核对结论：完成本次目标（Steam / GitHub 加速）所需的授权只有「管理员权限（hosts / 端口 / 根证书）」
与「自备或沿用加速数据源」两项；不需要任何 API Key、Access Token 或部署凭证。**

---

## 8. 验证步骤（装好 SDK 后执行）

```bash
# 0) 拉取保留的子模块（Titanium-Web-Proxy 是加速内核的硬依赖）
git submodule update --init --recursive

# 1) 确认 SDK
dotnet --version          # 期望 6.0.101（由 global.json 约束）

# 2) 还原 + 构建桌面端（Windows）
dotnet build src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj -c Debug

# 3) 构建加速内核所在的核心库（跨平台，最快暴露编译问题）
dotnet build src/ST.Client/ST.Client.csproj -c Debug

# 4) 单元测试
dotnet test tests/ST.Client.UnitTest/ST.Client.UnitTest.csproj      # 含 hosts 解析测试
dotnet test tests/Common.UnitTest/Common.UnitTest.csproj

# 5) 重新生成「残留引用」清单，逐条清理第 6 章待办
python tools/prune/dangling.py .

# 6) 运行时验证（Windows，管理员运行）
#    - 启动 → 勾选 Steam / GitHub 加速项目 → 启动加速
#    - 访问 https://github.com 与 Steam 社区页，确认可正常打开
#    - 查看日志中的「运行期设置已构建：… 匹配规则 N 条，脚本 M 个」
#    - 性能对比：连续刷新含大量子资源请求的页面，观察 DNS 查询次数
#      （ProxyDnsCache.Hits / Misses / Coalesced 计数器可直接取值验证缓存命中率）
```

---

## 9. 变更文件范围

### 9.1 删除（共 189 + 10 + 2 项，见 `tools/prune/*.py` 清单）

- **整个工程目录 41 个**：`source/`、移动端 12 个、服务端/短信 4 个、工具工程 10 个、
  控制台/UWP 桥/Demo/托盘 8 个、`ST.Services.CloudService.ViewModels`、
  `Avalonia.Diagnostics`、`ST.Client.Desktop`、4 个测试工程、8 个无用子模块工作树。
- **文件 100+ 个**：账号/令牌/成就/挂卡/游戏工具相关 VM、View、Service、Model、Setting、
  云服务 Client 与 DTO、`AutoMapperProfile`、`UploadFileContent` 等。

### 9.2 新增

```
tools/prune/closure.py                              # 工程闭包分析
tools/prune/prune.py                                # 三波幂等裁剪
tools/prune/dangling.py                             # 残留引用分析
tools/prune/finalize.py                             # 配置与解决方案收尾
REFACTOR_REPORT.md                                  # 本报告

src/ST.Client/Services/Accelerator/ProxyRuntimeSettings.cs      # 不可变设置快照 + 预编译脚本规则
src/ST.Client/Services/Accelerator/ProxyHostMatcher.cs          # 域名匹配索引
src/ST.Client/Services/Accelerator/ProxyDnsCache.cs             # TTL + single-flight DNS 缓存
src/ST.Client/Services/Accelerator/ProxyCertificateManager.cs   # 根证书管理
src/ST.Client/Services/Accelerator/ProxyRequestInterceptor.cs    # 请求改写
src/ST.Client/Services/Accelerator/ProxyScriptInjector.cs        # 脚本注入
src/ST.Client/Models/ScriptMappings.cs                           # 显式映射（替代 AutoMapper）
src/ST.Client/ServiceCollectionExtensions.AddGeneralLogging.cs    # 从死工程迁回
src/ST.Services.CloudService.Models/Models/ICloudServiceSettings.cs  # 精简后重建
```

### 9.3 重写 / 实质性修改

| 文件 | 说明 |
| --- | --- |
| `src/ST.Client/Services/Implementation/HttpProxyServiceImpl.cs` | 825 行巨型类 → 编排层 |
| `src/ST.Client/Services/IHttpProxyService.cs` | 移除 GOG / 语义反转成员 |
| `src/ST.Client/Services/IDnsAnalysisService.cs` | 移除 4 个死方法、4 组常量，补 `CancellationToken` |
| `src/ST.Client/Services/Implementation/ScriptManager.cs` | 去 AutoMapper、修 3 个缺陷、文件读写改异步 |
| `src/ST.Client.Desktop.Windows/Services/Implementation/DnsAnalysisServiceImpl.cs` | 去死方法、补取消、失败不抛异常 |
| `src/Common.CoreLib/Extensions/StringExtensions.cs` | `IsWildcard` 编译缓存 + 超时 |
| `src/ST.Services.CloudService/Services/CloudService/ApiConnection.cs` | 1037 → ≈640 行，只保留请求/响应/重试/下载 |
| `src/ST.Services.CloudService/Services/CloudService/CloudServiceClientBase.cs` | 8 子客户端 → 3 |
| `src/ST.Services.CloudService/Services/CloudService/IApiConnectionPlatformHelper.cs` | 去账号鉴权能力 |
| `src/ST.Services.CloudService/Services/ICloudServiceClient.cs` | 去 5 个子客户端 + 废弃 `Forward` |
| `src/ST.Services.CloudService/ServiceCollectionExtensions.cs` | 去 Mock 分支 |
| `src/ST.Client/Services/Implementation/CloudServiceClient.cs` | 去 `IUserManager` 依赖 |
| `src/ST.Client/Models/AppSettings.cs` | 去 AES/RSA，官方渠道判定简化 |
| `src/ST.Client/ServiceCollectionExtensions.cs` | 注册项大幅精简 |
| `src/Startup2.cs` | 组合根重写，去条件编译与无关服务 |
| `src/ST.Client/DILevel.cs`、`StartupOptions.cs` | 移除 `Steam` 标志位 |
| `src/ST.Client/Repositories/Implementation/ScriptRepository.cs` | 去 AutoMapper 引用 |
| `ST.Client.csproj`、`ST.csproj`、`ST.Services.CloudService*.csproj`、三个平台 csproj | 依赖与引用精简 |
| `Directory.Packages.props`、`.gitmodules`、`SteamToolsV2+.sln` | 配置与解决方案收敛 |

---

## 10. 风险与建议

1. **未经编译器验证**：本轮所有改动均未经过 `dotnet build`（环境无 SDK）。
   第 6 章的待办清单是「已知会编译失败」的部分，但可能仍有个别遗漏。
   建议先执行第 8 章的步骤 3（构建 `ST.Client.csproj`，依赖最少、失败最快）。
2. **`OnCertificateValidation` 放宽上游 TLS 校验**（`e.IsValid = true`）是原实现的既有取舍，
   为兼容自建/镜像加速节点而设，本次**未改动**，但在重构中补了注释说明。
   若你只使用官方节点，可考虑改为按错误类型白名单放行。
3. **加速数据源是第三方官方后端**。若用于长期个人使用问题不大；
   若二次分发，建议自建后端替换 `Startup.SetApiBaseUrl`，以免给上游带来压力或触及 ToS。
4. **`AccelerateProjectGroupDTO` 的 `Items` / `ObservableItems` 双列表**仍在，
   存在同量级内存重复。改动它会牵动 Avalonia 绑定行为，建议在能跑起来之后，
   用 `dotnet-counters` 或 `Process Explorer` 实测各加速项目组的对象占用，再决定是否收敛。
5. **`ST.Services.CloudService.Models` 下仍有约 20 个账号侧 DTO 文件未删**，
   属于零风险的进一步清理项（保留不影响编译）。
