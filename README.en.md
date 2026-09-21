<div align="center">

# Steam / GitHub Accelerator (Personal Trimmed Build)

**A Steam++ fork that keeps only network acceleration** · Personal use · Windows-first

</div>

---

## What this is

This repository is a **fork** of [BeyondDimension/SteamTools](https://github.com/BeyondDimension/SteamTools)
(Steam++ / Watt Toolkit), heavily trimmed to keep **only Steam and GitHub network acceleration**.
Everything else has been removed.

Upstream is a full "Steam toolbox": account management, Steam Guard tokens, achievement unlocking,
ASF card farming, game utilities, multi-platform proxying and more. None of that is needed here —
the goal is a small, maintainable tool that accelerates Steam and GitHub and nothing else.

> See **[REFACTOR_REPORT.md](./REFACTOR_REPORT.md)** for the full architecture analysis, removal scope,
> optimization work and defect list; see **[NEXT-STEPS.md](./NEXT-STEPS.md)** for current status and pending tasks.

## ✨ Retained features

| Feature | Description |
| --- | --- |
| **Accelerate project toggles** | Fetches the project list from the server (Steam, GitHub, …) and enables the ones you select |
| **Local reverse proxy** | Built on [Titanium.Web.Proxy](https://github.com/justcoding121/titanium-web-proxy); matches by domain, then rewrites upstream IP / port / TLS SNI |
| **System proxy mode** | Writes the system proxy settings so browsers and the Steam client are covered globally |
| **Transparent proxy mode (hosts)** | Leaves the system proxy alone and instead points accelerated domains at `127.0.0.1` via hosts |
| **User script injection** | Fetch scripts from the script store or import local JS files; injected into proxied pages |
| **Upstream (second-level) proxy** | Optionally chains accelerated traffic through another HTTP proxy |
| **Upstream DNS selection** | Presets for AliDNS / 114 / DNSPod / Baidu / Google / Cloudflare |
| **Root certificate management** | Creates, installs and checks the local root certificate used for HTTPS decryption |

## ❌ Removed compared to upstream

Accounts and SMS verification, Steam Guard tokens, achievement unlocking and management,
ArchiSteamFarm card farming, game utilities (borderless windowing / idling), GOG and other
platform proxying, announcements and notifications, app updates, donation ranking,
mobile (Android / iOS / Xamarin.Forms), the server-side and SMS services, all build-tool projects,
and upstream's legacy V1 codebase (`source/`).

Project count went from **58 → 22**, C# files under `src` from **1203 → ~570**,
and git submodules from **13 → 5**.

## 🖥 Requirements

- **Windows 10 / 11** (primary target, best verified)
- The Linux / macOS projects are kept and compile, but have **not been runtime-verified**

## ⌨️ Building

Requires **.NET SDK 6.0.101** (pinned by `global.json`). A one-command, admin-free script is provided:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1

# Release build plus unit tests
powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Configuration Release -Test
```

The script handles three non-obvious prerequisites that otherwise break the build with
misleading errors:

1. If SDK 6.0.101 is missing, it downloads and extracts it into your user directory
   (**no registry writes, no PATH changes, no admin rights**);
2. It initialises submodules and fetches the **full git history** for `references/reactive` (Rx.NET) —
   it uses Nerdbank.GitVersioning, and a shallow clone breaks the build. It also enables
   `core.longpaths` for that submodule (it contains a path exceeding the Windows MAX_PATH limit);
3. NuGet restore runs with `--disable-parallel` to avoid an intermittent
   `Access to the path ... is denied` race.

Equivalent manual commands:

```bash
git submodule update --init --recursive
dotnet restore src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj --disable-parallel
dotnet build   src/ST.Client.Desktop.Avalonia.App/ST.Client.Avalonia.App.csproj -c Debug --no-restore
```

> **Note**: the original `AvaloniaCI` feed in `nuget.config` (`nuget.avaloniaui.net`) is dead
> (HTTP 521) and made restore fail outright. It has been removed in favour of nuget.org.

## ▶️ Running

**Administrator rights are required** for:

- Binding ports **443 / 80** (transparent mode; on Windows a busy port 80 is skipped gracefully)
- Writing the **hosts** file (transparent proxy mode)
- Installing the **root certificate** into the system trust store (HTTPS decryption)

## 🌐 Where acceleration data comes from

The accelerate project list comes from anonymous server endpoints (default `https://api.steampp.net`):

```
api/Accelerate/All           accelerate project groups (domains, upstream port, SNI, proxy type…)
api/script/basics            built-in script
api/script/table/*           script store
api/script/updates           script updates
api/version/checkupdate3/*   version policy
```

These are **anonymous endpoints — no login required**. The base URL is set in
`SetApiBaseUrl` inside `src/Startup2.cs`; change that single place to point at your own backend.

> This project no longer needs any embedded keys (`aes-key.pfx` / `rsa-public-key.pfx`).
> Upstream used them to encrypt account-API traffic; they were removed along with the account module.

## 🔧 Known limitations

- **Not runtime-verified yet**: the code compiles cleanly (25/25 projects, 13/13 unit tests)
  but a **full acceleration run has not been exercised in a real environment**
  (proxy / certificate / hosts). See P0-2 in NEXT-STEPS.
- **Upstream TLS validation is relaxed**: `OnCertificateValidation` sets `e.IsValid = true`.
  This is an **upstream trade-off** (to support self-hosted / mirror nodes) and was **not changed** here.
  If you only use official nodes, consider tightening it to an error-type allowlist.
- **Packaging is incomplete**: scripts depending on the removed tool projects have been cleaned up and
  `build.ps1` retains publish support; upstream's Store / UWP packaging flow is not kept.
- **No auto-update**: the update module was removed; rebuild manually to update.

## 📚 Documentation

| Document | Contents |
| --- | --- |
| [REFACTOR_REPORT.md](./REFACTOR_REPORT.md) | Architecture analysis, removal scope, structure / memory / defect work, permissions, verification steps |
| [NEXT-STEPS.md](./NEXT-STEPS.md) | Current status, P0–P3 tasks, dependencies, open decisions |
| [src/README.md](./src/README.md) | Project layout after trimming |
| [tools/build.ps1](./tools/build.ps1) | One-command build (handles environment prerequisites) |
| [tools/prune/](./tools/prune/) | Re-runnable tooling used for the trim (project closure, pruning, dangling scan, config cleanup) |

## 📄 License and credits

This project keeps upstream's **GNU General Public License v3.0** (see [LICENSE](./LICENSE))
and retains the original project's copyright and attribution.

Thanks to (full list lives in the upstream repository):

- [BeyondDimension/SteamTools](https://github.com/BeyondDimension/SteamTools) — the origin of this fork
- [justcoding121/titanium-web-proxy](https://github.com/justcoding121/titanium-web-proxy) — reverse proxy engine
- [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) and [amwx/FluentAvalonia](https://github.com/amwx/FluentAvalonia) — desktop UI
- [reactiveui/ReactiveUI](https://github.com/reactiveui/ReactiveUI) and [dotnet/reactive](https://github.com/dotnet/reactive) — MVVM and reactive
- [praeclarum/sqlite-net](https://github.com/praeclarum/sqlite-net) — local storage
