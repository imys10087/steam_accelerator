# 源码结构与工程职责

> 本文档描述**精简后**的结构。原先 58 个工程已裁剪为 **22 个（`src/`）+ 3 个（`tests/`）**。
> 裁剪范围与原因见根目录 [REFACTOR_REPORT.md](../REFACTOR_REPORT.md)。

## 🏗️ 项目结构

```
src/
├── ST.Client.Desktop.Avalonia.App/     桌面应用入口
├── ST.Client.Desktop.Avalonia/         Avalonia View 层
├── ST.Client.Desktop.Windows/          Windows 平台实现
├── ST.Client.Desktop.Mac/              macOS 平台实现
├── ST.Client.Desktop.Linux/            Linux 平台实现
│
├── ST.Client/                          客户端通用类库（加速内核所在）
├── ST.Services.CloudService/           服务端接口层
├── ST.Services.CloudService.Models/    服务端 DTO
│
├── ST/                                 基础运行库
├── Common.CoreLib/                     跨端通用基础库
├── Common.ClientLib/                   客户端基础库
├── Common.AreaLib/                     地区数据
├── Common.PinyinLib/                   拼音抽象
├── Common.PinyinLib.ChnCharInfo/       拼音实现（桌面）
├── Common.PinyinLib.TinyPinyin/        拼音实现（备选）
├── Repositories.sqlite-net-pcl/        仓储与本地持久化
│
└── Avalonia.*/                         Avalonia 定制 / 后端（6 个工程）

tests/
├── ST.Client.UnitTest/                 客户端单元测试
├── ST.Client.UnitTest.Resources/       测试用资源（hosts 期望值等）
└── Common.UnitTest/                    基础库单元测试
```

## 🗂️ 各工程职责

### 应用与平台层

| 工程 | 职责 |
| --- | --- |
| **ST.Client.Desktop.Avalonia.App** | 桌面应用入口。组合根（`Startup2.cs`）、`Program`、`App`、命令行参数处理、托盘 `NotifyIconHelper` |
| **ST.Client.Desktop.Avalonia** | Avalonia View 层：页面（加速 / 脚本 / 设置 / 关于）、窗口、控件、值转换器、样式与主题资源 |
| **ST.Client.Desktop.Windows** | Windows 平台实现：注册表读写、DPAPI 数据保护、`DnsClient` 解析、窗口与跳转列表、`NotifyIcon` |
| **ST.Client.Desktop.Mac** | macOS 平台实现（`IPlatformService`、DNS、证书信任、.app 打包支持） |
| **ST.Client.Desktop.Linux** | Linux 平台实现（`IPlatformService`、DNS、证书信任、端口权限降级） |

### 客户端核心

| 工程 | 职责 |
| --- | --- |
| **ST.Client** | 客户端通用类库，**加速内核所在**。含 `Services/Accelerator/`（`ProxyHostMatcher` 域名匹配、`ProxyDnsCache` DNS 缓存、`ProxyCertificateManager` 证书、`ProxyRequestInterceptor` 请求改写、`ProxyScriptInjector` 脚本注入、`ProxyRuntimeSettings` 不可变快照）、`HttpProxyServiceImpl` 编排层、`ScriptManager` 脚本管理、`ProxyService` 对外服务、平台与设置抽象 |
| **ST.Services.CloudService** | 服务端接口层：`ApiConnection`（请求 / 响应 / 重试 / 下载）、`AccelerateClient` / `ScriptClient` / `VersionClient`、DI 扩展 |
| **ST.Services.CloudService.Models** | 服务端 DTO（加速项目、脚本、版本），带 ReactiveObject 能力供 UI 绑定 |

### 通用基础

| 工程 | 职责 |
| --- | --- |
| **ST** | 基础运行库：加密工具（`AESUtils` / `RSAUtils` / `Hashs`）、`Properties`（`ThisAssembly`）、DI 基础设施、通用常量 |
| **Common.CoreLib** | 跨端通用基础：文件与路径抽象（`IOPath`）、扩展方法（含 `IsWildcard`）、`IHttpPlatformHelperService`、序列化 |
| **Common.ClientLib** | 客户端基础：DI 注册扩展、平台服务抽象（`IPlatformService` / `IDnsAnalysisService` / `IHostsFileService`）、数据保护链（`ILocalDataProtectionProvider`）、`IToast`、偏好存储 |
| **Common.AreaLib** | 地区（国家 / 行政区）数据与查询 |
| **Common.PinyinLib** | 拼音抽象（`IPinyin`），用于脚本列表的中文搜索 |
| **Common.PinyinLib.ChnCharInfo** | 桌面平台拼音实现 |
| **Common.PinyinLib.TinyPinyin** | 备选拼音实现（已移除 `MonoAndroid11.0` 目标框架，只保留 `netstandard2.1`） |
| **Repositories.sqlite-net-pcl** | 基于 sqlite-net 的仓储实现与实体持久化（脚本仓储） |

### Avalonia 定制 / 后端

上游为适配自身需求对 Avalonia 做了定制并 vendor 进仓库，这 6 个工程随 `references/` 子模块一起保留：

`Avalonia.Native`、`Avalonia.Skia.Internals`、`Avalonia.Themes.Default`、`Avalonia.Themes.Fluent`、`Avalonia.Win32`、`Avalonia.X11`

> 常规 UI 开发不需要改动它们。

## 📁 关于命名空间

所有工程仍沿用上游的 `System.Application` 根命名空间与 `DisableImplicitNamespaceImports` 约定，
因此**每个 `.cs` 文件都必须显式写出 `using`**。本次精简**未做命名空间重写**，以降低回归风险。

## 📁 存储空间

| 用途 | 位置 |
| --- | --- |
| 应用数据（脚本、配置） | `IOPath.AppDataDirectory` |
| 缓存（脚本构建产物、证书） | `IOPath.CacheDirectory` |
| 日志 | `IOPath.CacheDirectory/Logs`（`nlog-all-*.log`） |
| 数据库 | SQLite（脚本仓储） |

## 🧪 测试

```bash
dotnet test tests/ST.Client.UnitTest/ST.Client.UnitTest.csproj
dotnet test tests/Common.UnitTest/Common.UnitTest.csproj
```

当前 `ST.Client.UnitTest` 共 13 个测试全部通过，其中：

- `HostsFileTest` —— hosts 文件解析（透明代理模式的关键逻辑），期望值来自 `ST.Client.UnitTest.Resources`
- `DataProtectionRegistrationTest` —— 锁定数据保护注册链，防止 `ILocalDataProtectionProvider` 被误删
  （这类缺失**编译器发现不了**，只会在运行期抛 DI 异常）
- `HttpTest.GetImage` / `ModelsTest` —— 图片缓存与序列化冒烟

> ⚠️ 注意：现有测试覆盖的是基础库与注册链，**并不验证加速功能本身**（代理改写、证书信任、hosts 生效）。
