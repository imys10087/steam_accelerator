<div align="center">

# Steam / GitHub 加速器（自用精简版）

**仅保留「网络加速」的 Steam++ 精简分支** · 个人自用 · Windows 优先

</div>

---

## 这是什么

本仓库是 [BeyondDimension/SteamTools](https://github.com/BeyondDimension/SteamTools)（Steam++ / Watt Toolkit）的一个 **Fork**，经过大幅裁剪，**只保留 Steam 与 GitHub 的网络加速功能**，其余模块全部移除。

原始项目的定位是「Steam 工具箱」——包含账号管理、令牌（Steam Guard）、成就解锁、ASF 挂卡、游戏工具、多平台代理等一大堆功能。这些对本项目**都不需要**：我只要一个干净、能加速 Steam 与 GitHub 的工具，代码越少越好维护。

> 完整的技术分析、精简范围、优化项与缺陷修复清单见 **[REFACTOR_REPORT.md](./REFACTOR_REPORT.md)**；
> 当前进度与后续待办见 **[NEXT-STEPS.md](./NEXT-STEPS.md)**。

## ✨ 保留的功能

| 功能 | 说明 |
| --- | --- |
| **加速项目开关** | 从服务端拉取加速项目列表（含 Steam、GitHub 等），按需勾选启用 |
| **本地反向代理** | 基于 [Titanium.Web.Proxy](https://github.com/justcoding121/titanium-web-proxy) 实现，按域名匹配后改写上游 IP / 端口 / TLS SNI |
| **系统代理模式** | 写入系统代理设置，浏览器与 Steam 客户端全局生效 |
| **透明代理模式（Hosts）** | 不改系统代理，而是把加速域名写入 hosts 指向 `127.0.0.1` |
| **用户脚本注入** | 从脚本商店获取 / 本地导入 JS 脚本，注入到被代理的页面 |
| **上游代理（二级代理）** | 支持为加速流量再套一层 HTTP 代理 |
| **上游 DNS 选择** | 内置阿里 / 114 / DNSPod / 百度 / Google / Cloudflare 等预设 |
| **根证书管理** | 自动创建、安装、检测用于 HTTPS 解密的本地根证书 |

## ❌ 相比上游移除的内容

账号体系与短信验证、Steam Guard 令牌、成就解锁与管理、ArchiSteamFarm 挂卡、游戏工具（强制窗口化 / 挂卡）、GOG 与其他平台代理、公告与通知播报、应用更新、捐赠排行、移动端（Android / iOS / Xamarin.Forms）、服务端与短信服务、全部构建工具工程、以及上游 V1 的遗留代码库（`source/`）。

工程数从 **58 → 22**，`src` 下 C# 文件从 **1203 → 约 570**，Git 子模块从 **13 → 5**。

## 🖥 系统要求

- **Windows 10 / 11**（主要目标平台，验证最充分）
- Linux / macOS 的工程仍保留并可通过编译，但**未做运行验证**

## ⌨️ 构建

需要 **.NET SDK 6.0.101**（由 `global.json` 钉死）。仓库提供了免管理员的一键脚本：

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1

# 构建 Release 并跑单元测试
powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Configuration Release -Test
```

脚本会自动处理三个非显然的前置条件（否则构建会失败，且报错信息不指向真正原因）：

1. 若本机没有 SDK 6.0.101，会下载并解压到用户目录（**不写注册表、不改 PATH、不需要管理员**）；
2. 拉取子模块，并为 `references/reactive`（Rx.NET）拉**完整 git 历史** —— 它使用 Nerdbank.GitVersioning，浅克隆会导致构建失败；同时为它单独开启 `core.longpaths`（内含一个超出 Windows MAX_PATH 的文件）；
3. NuGet 还原加 `--disable-parallel`，规避并行还原偶发的 `Access to the path ... is denied` 竞态。

手动构建等价命令：

```bash
git submodule update --init --recursive
dotnet restore src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj --disable-parallel
dotnet build   src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj -c Debug --no-restore
```

> **注意**：`nuget.config` 中原有的 `AvaloniaCI` 源（`nuget.avaloniaui.net`）已下线（返回 HTTP 521），会导致还原直接失败，因此已移除并改为显式指向 nuget.org。

## ▶️ 运行

**需要管理员权限**，因为要用到以下系统能力：

- 监听 **443 / 80** 端口（透明模式；Windows 下 80 端口被占用时会降级跳过）
- 写入 **hosts** 文件（透明代理模式）
- 安装**根证书**到系统信任存储（HTTPS 解密）

## 🌐 加速数据来源

加速项目列表来自服务端匿名接口（默认 `https://api.steampp.net`）：

```
api/Accelerate/All           加速项目组（域名、上游端口、SNI、代理类型…）
api/script/basics            内置脚本
api/script/table/*           脚本商店
api/script/updates           脚本更新
api/version/checkupdate3/*   版本策略
```

这些接口**均为匿名接口，不需要登录**。默认地址在 `src/Startup2.cs` 的 `SetApiBaseUrl` 中设置；如需改用自建后端，改这一处即可。

> 本项目不再需要任何内嵌密钥（`aes-key.pfx` / `rsa-public-key.pfx`）——
> 上游把它们用于账号接口的传输加密，已随账号模块一起移除。

## 🔧 已知限制

- **运行期尚未验证**：代码已通过全量编译（逐工程 25/25、单元测试 13/13），但**尚未在真实环境跑通一次完整加速流程**（代理 / 证书 / hosts）。详见 NEXT-STEPS 的 P0-2。
- **上游 TLS 校验被放宽**：`OnCertificateValidation` 中设置 `e.IsValid = true`，这是**上游原有的取舍**（为兼容自建 / 镜像加速节点），本项目**未改动**。若只用官方节点，可考虑收紧为按错误类型白名单。
- **打包链路不完整**：`packaging/` 中依赖已移除工具工程的脚本已清理，`build.ps1` 保留发布能力；上游的 Store / UWP 打包流程未保留。
- **无自动更新**：应用更新模块已移除，更新需手动重新构建。

## 📚 文档索引

| 文档 | 内容 |
| --- | --- |
| [REFACTOR_REPORT.md](./REFACTOR_REPORT.md) | 架构分析、精简范围、结构 / 内存 / 缺陷优化、授权清单、验证步骤 |
| [NEXT-STEPS.md](./NEXT-STEPS.md) | 当前进度、P0~P3 待办、依赖关系、待决策点 |
| [src/README.md](./src/README.md) | 精简后的工程结构与各自职责 |
| [tools/build.ps1](./tools/build.ps1) | 一键构建（含环境前置条件处理） |
| [tools/prune/](./tools/prune/) | 本次精简所用的可复跑工具链（工程闭包分析 / 裁剪 / 残留扫描 / 配置收尾） |

## 📄 许可证与致谢

本项目沿用上游的 **GNU General Public License v3.0**（见 [LICENSE](./LICENSE)），并保留原始项目的版权与署名。

感谢以下项目（完整清单见上游仓库）：

- [BeyondDimension/SteamTools](https://github.com/BeyondDimension/SteamTools) —— 本 Fork 的源头
- [justcoding121/titanium-web-proxy](https://github.com/justcoding121/titanium-web-proxy) —— 本地反向代理引擎
- [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) 与 [amwx/FluentAvalonia](https://github.com/amwx/FluentAvalonia) —— 桌面 UI
- [reactiveui/ReactiveUI](https://github.com/reactiveui/ReactiveUI) 与 [dotnet/reactive](https://github.com/dotnet/reactive) —— MVVM 与响应式
- [praeclarum/sqlite-net](https://github.com/praeclarum/sqlite-net) —— 本地存储
