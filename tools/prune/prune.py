# -*- coding: utf-8 -*-
"""Phase 1 prune: remove every non-acceleration module from the fork.

Keeps ONLY:
  * Steam 社区/商店 反向代理 与 hosts/DNS 加速
  * GitHub 加速
  * 代理脚本(JS 注入) 与脚本商店
  * 系统代理 / 证书管理 / 二级代理 / SOCKS5

Removes: 账号体系、短信验证、令牌、成就、挂卡(ASF)、游戏工具、移动端、
服务端、打包/翻译等构建工具、V1 遗留代码，以及它们的依赖与配置。

Idempotent; prints a manifest.  Run:
    python tools/prune/prune.py <repo-root> [--dry-run]
"""
from __future__ import annotations

import argparse
import os
import shutil
import sys
from pathlib import Path

# --------------------------------------------------------------------------
# 1. whole project / directory removals
# --------------------------------------------------------------------------
DROP_DIRS: tuple[str, ...] = (
    "source",                                      # 遗留 V1 代码库
    # ---- 移动端 ----
    "src/ST.Client.Android",
    "src/ST.Client.iOS",
    "src/ST.Client.Mobile",
    "src/ST.Client.Mobile.Droid",
    "src/ST.Client.Mobile.Droid.App",
    "src/ST.Client.Mobile.Droid.Design",
    "src/ST.Client.Mobile.Droid.Resources",
    "src/ST.Client.Mobile.iOS",
    "src/ST.Client.Mobile.iOS.App",
    "src/ST.Client.XamarinForms",
    "src/Common.ClientLib.Droid",
    "src/Common.ClientLib.iOS",
    # ---- 服务端 / 短信 ----
    "src/Common.ServerLib",
    "src/Services.SmsSender",
    "src/ST.Server.Resources",
    "src/Repositories.EFCore",
    # ---- 构建/发布/翻译工具工程 ----
    "src/ST.Tools.AndroidResourceLink",
    "src/ST.Tools.AreaImport",
    "src/ST.Tools.DesktopBridgeLink",
    "src/ST.Tools.MinifyStaticSites",
    "src/ST.Tools.OpenSourceLibraryList",
    "src/ST.Tools.Packager",
    "src/ST.Tools.Packager.InstallerSetup",
    "src/ST.Tools.Publish",
    "src/ST.Tools.Translate",
    "src/ST.Tools.Win7Troubleshoot",
    # ---- 控制台 / UWP 桥 / Demo / Linux 托盘宿主 ----
    "src/ST.Client.Desktop.Console.App",
    "src/ST.Client.Desktop.Console.App.Bridge",
    "src/ST.Client.Desktop.Avalonia.App.Bridge",
    "src/ST.Client.Desktop.Avalonia.App.Bridge.Package",
    "src/ST.Client.Desktop.Avalonia.App.Bridge.Package.RefLauncher",
    "src/ST.Client.Desktop.Avalonia.Demo.App",
    "src/ST.Client.Desktop.Linux.App.TrayIcon",
    "src/ST.Client.Desktop.Mac.Native",
    # ---- 重复编译的 DTO 工程 ----
    "src/ST.Services.CloudService.ViewModels",
    # ---- 未使用的 Avalonia fork 副本 ----
    "src/Avalonia.Diagnostics",
    # ---- 无关测试工程 ----
    "tests/Common.UnitTest.Droid",
    "tests/Common.UnitTest.Droid.App",
    "tests/ST.Client.Desktop.UnitTest",
    "tests/ST.Client.UnitTest.Resources",
    # ---- 不再需要的子模块工作树 ----
    "references/MetroRadiance",
    "references/SteamAchievementManager",
    "references/Steam4NET",
    "references/Depressurizer",
    "references/WinAuth",
    "references/ArchiSteamFarm",
    "references/dotnet-packaging",
    "references/SevenZipSharp",
    # ---- 完全未被引用的历史遗留工程（内含与 ST.Client 重名的重复类型，
    #      且引用了早已不存在的 ProxySettings.HostProxyPortId / IHttpProxyService.IsWindowsProxy） ----
    "src/ST.Client.Desktop",
)

# --------------------------------------------------------------------------
# 2c. wave-3: 裁剪后暴露出的死文件
# --------------------------------------------------------------------------
DROP_FILES_3: tuple[str, ...] = (
    # Steam 成就相关的属性表读取器
    "src/ST.Client/SteamAppPropertyHelper.cs",
    # 仅自引用的独立工具类
    "src/ST.Client/WebProxyHelper.cs",
    # 仅被已删除的「关机」窗口使用的枚举
    "src/ST.Client/Models/Enums/SystemEndMode.cs",
    # 通知/播报体系（原用于上报活跃用户与公告，属账号侧功能）
    "src/ST.Client/Services/Implementation/NotificationServiceImpl.cs",
    "src/ST.Client/Services/INotificationService.cs",
    "src/ST.Client/NotificationChannelType.cs",
    "src/ST/NotificationType.cs",
    "src/ST.Client/Extensions/NotificationType_Channel_EnumExtensions.cs",
    # 账号快速登录渠道
    "src/ST/FastLoginChannel.cs",
)

# --------------------------------------------------------------------------
# 2. file removals inside kept projects
# --------------------------------------------------------------------------
_ST = "src/ST.Client"
_AZ = "src/ST.Client.Desktop.Avalonia"
_CS = "src/ST.Services.CloudService"

DROP_FILES: tuple[str, ...] = (
    # ============ ST.Client : 账号 / 令牌 / 成就 / 挂卡 / 游戏工具 页面 ======
    f"{_ST}/UI/ViewModels/Pages/ArchiSteamFarmPlus",
    f"{_ST}/UI/ViewModels/Pages/GameRelated",
    f"{_ST}/UI/ViewModels/Pages/SteamAccountPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/GameListPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/LocalAuthPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/LocalAuthPageViewModel.Mobile.cs",
    f"{_ST}/UI/ViewModels/Pages/SteamIdlePageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/OtherPlatformPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/ExplorerPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/MyPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/DebugPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Pages/StartPageViewModel.cs",
    f"{_ST}/UI/ViewModels/Windows/ArchiSteamFarmPlus",
    f"{_ST}/UI/ViewModels/Windows/GameListPage",
    f"{_ST}/UI/ViewModels/Windows/LocalAuthPage",
    f"{_ST}/UI/ViewModels/Windows/SteamAccountPage",
    f"{_ST}/UI/ViewModels/Windows/BindPhoneNumberWindowViewModel.cs",
    f"{_ST}/UI/ViewModels/Windows/ChangeBindPhoneNumberWindowViewModel.cs",
    f"{_ST}/UI/ViewModels/Windows/LoginOrRegisterWindowViewModel.cs",
    f"{_ST}/UI/ViewModels/Windows/LoginOrRegisterWindowViewModel.Mobile.cs",
    f"{_ST}/UI/ViewModels/Windows/UserProfileWindowViewModel.cs",
    f"{_ST}/UI/ViewModels/Windows/UserProfileWindowViewModel.Mobile.cs",
    f"{_ST}/UI/ViewModels/SendSmsUIHelper.cs",
    f"{_ST}/UI/ViewModels/ThirdPartyLoginHelper.cs",
    f"{_ST}/UI/QRCodeHelper.cs",
    f"{_ST}/UI/QRCodeHelper.Net.Codecrete.QrCodeGenerator.cs",
    f"{_ST}/UI/QRCodeHelper.Net.Codecrete.QrCodeGenerator.SkiaSharp.cs",
    f"{_ST}/UI/QRCodeHelper.QRCoder.cs",
    f"{_ST}/UI/AuthorizeAttribute.cs",
    # ---- ST.Client : 关闭的账号/Steam 服务与仓储 ----
    f"{_ST}/Services/Implementation/ArchiSteamFarmServiceImpl.cs",
    f"{_ST}/Services/Implementation/ArchiSteamFarmServiceImpl.UIPack.cs",
    f"{_ST}/Services/Implementation/SteamDbWebApiServiceImpl.cs",
    f"{_ST}/Services/Implementation/SteamworksLocalApiServiceImpl.cs",
    f"{_ST}/Services/Implementation/SteamworksWebApiServiceImpl.cs",
    f"{_ST}/Services/Implementation/UserManager.cs",
    f"{_ST}/Services/Implementation/ApplicationUpdateServiceBaseImpl.cs",
    f"{_ST}/Services/Implementation/EmbeddedAesDataProtectionProvider.cs",
    f"{_ST}/Services/Implementation/EmptyLocalDataProtectionProvider.cs",
    f"{_ST}/Services/Implementation/GeneralLocalDataProtectionProvider.cs",
    f"{_ST}/Services/Implementation/LocalDataProtectionProvider.cs",
    f"{_ST}/Services/IArchiSteamFarmService.cs",
    f"{_ST}/Services/IArchiSteamFarmService.lib_impl.cs",
    f"{_ST}/Services/IBiometricService.cs",
    f"{_ST}/Services/IJumpListService.cs",
    f"{_ST}/Services/ISevenZipHelper.cs",
    f"{_ST}/Services/ISteamDbWebApiService.cs",
    f"{_ST}/Services/ISteamService.cs",
    f"{_ST}/Services/ISteamworksLocalApiService.cs",
    f"{_ST}/Services/ISteamworksWebApiService.cs",
    f"{_ST}/Services/IUserManager.cs",
    f"{_ST}/Services/IApplicationUpdateService.cs",
    # ---- ST.Client : Steam 账号/成就模型 ----
    f"{_ST}/Models/ArchiSteamFarmCommand.cs",
    f"{_ST}/Models/AuthorizedDevice.cs",
    f"{_ST}/Models/ImportedSDAEntry.cs",
    f"{_ST}/Models/MyAuthenticator.cs",
    f"{_ST}/Models/MyAuthenticator.Mobile.cs",
    f"{_ST}/Models/CurrentUser.cs",
    f"{_ST}/Models/Steam",
    f"{_ST}/SteamApiUrls.cs",
    f"{_ST}/ImageUrlHelper.cs",
    # ---- ST.Client : 不再使用的设置项 ----
    f"{_ST}/Settings/ASFSettings.cs",
    f"{_ST}/Settings/ASFSettings.Avalonia.cs",
    f"{_ST}/Settings/GameLibrarySettings.cs",
    f"{_ST}/Settings/GameLibrarySettings.Avalonia.cs",
    f"{_ST}/Settings/SteamAccountSettings.cs",
    f"{_ST}/Settings/SteamSettings.cs",

    # ============ ST.Client.Desktop =========================================
    "src/ST.Client.Desktop/UI/ViewModels/Pages/ArchiSteamFarmPlusPageViewModel.cs",
    "src/ST.Client.Desktop/UI/ViewModels/Pages/LocalAuthPageViewModel.shared.cs",

    # ============ ST.Client.Desktop.Avalonia : 视图 =========================
    f"{_AZ}/Application/UI/Views/Pages/ArchiSteamFarmPlus",
    f"{_AZ}/Application/UI/Views/Pages/GameRelated",
    f"{_AZ}/Application/UI/Views/Pages/DebugPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/DebugPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/DebugWebViewPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/DebugWebViewPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/GameListPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/GameListPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/LocalAuthPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/LocalAuthPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/StartPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/StartPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/SteamAccountPage.axaml",
    f"{_AZ}/Application/UI/Views/Pages/SteamAccountPage.axaml.cs",
    f"{_AZ}/Application/UI/Views/Pages/Settings/Settings_Steam.axaml",
    f"{_AZ}/Application/UI/Views/Pages/Settings/Settings_Steam.axaml.cs",
    f"{_AZ}/Application/UI/Views/Windows/ArchiSteamFarmPlus",
    f"{_AZ}/Application/UI/Views/Windows/GameListPage",
    f"{_AZ}/Application/UI/Views/Windows/LocalAuthPage",
    f"{_AZ}/Application/UI/Views/Windows/SteamAccountPage",
    f"{_AZ}/Application/UI/Views/Windows/UserProfile",
    f"{_AZ}/Application/UI/Views/Windows/DebugWindow.axaml",
    f"{_AZ}/Application/UI/Views/Windows/DebugWindow.axaml.cs",
    f"{_AZ}/Application/UI/Views/Windows/NewVersionWindow.axaml",
    f"{_AZ}/Application/UI/Views/Windows/NewVersionWindow.axaml.cs",
    f"{_AZ}/Application/UI/Views/Windows/WebView3Window.axaml",
    f"{_AZ}/Application/UI/Views/Windows/WebView3Window.axaml.cs",
    f"{_AZ}/Application/UI/Views/Controls/WebView3.axaml",
    f"{_AZ}/Application/UI/Views/Controls/WebView3.axaml.cs",
    f"{_AZ}/Application/UI/Views/Controls/WebViewBase.cs",
    f"{_AZ}/Application/UI/Views/Controls/EmbedSample.cs",
    f"{_AZ}/Application/UI/Views/Controls/UserControl/ConsoleShell.axaml",
    f"{_AZ}/Application/UI/Views/Controls/UserControl/ConsoleShell.axaml.cs",
    # ---- Avalonia : 账号/更新/Cef 相关代码 ----
    f"{_AZ}/Application/Converters/ASF",
    f"{_AZ}/Application/Services/Implementation/AvaloniaApplicationUpdateServiceImpl.cs",
    f"{_AZ}/ServiceCollectionExtensions.AddApplicationUpdateService.cs",
    f"{_AZ}/Application/UI/CefNetApp.cs",
    f"{_AZ}/CefSharp",
    f"{_AZ}/Extensions/CookieExtensions.cs",
    f"{_AZ}/Extensions/WebViewExtensions.cs",

    # ============ ST.Services.CloudService : 账号体系 ======================
    f"{_CS}/Models/GAPAuthenticators",
    f"{_CS}/WinAuth",
    f"{_CS}/Services/CloudService/Clients/AccountClient.cs",
    f"{_CS}/Services/CloudService/Clients/ActiveUserClient.cs",
    f"{_CS}/Services/CloudService/Clients/AuthMessageClient.cs",
    f"{_CS}/Services/CloudService/Clients/AuthMessageClientHelper.cs",
    f"{_CS}/Services/CloudService/Clients/DonateRankingClient.cs",
    f"{_CS}/Services/CloudService/Clients/ManageClient.cs",
    f"{_CS}/Services/CloudService/Clients/NoticeClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/IAccountClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/IActiveUserClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/IAuthMessageClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/IDonateRankingClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/IManageClient.cs",
    f"{_CS}/Services/CloudService/Clients/Abstractions/INoticeClient.cs",
    f"{_CS}/Services/IAuthHelper.cs",
    f"{_CS}/Models/UploadFileSource.cs",
    f"{_CS}/Models/IUploadFileSource.cs",
    f"{_CS}/UploadFileType.cs",
    f"{_CS}/UploadFileContent.cs",
    f"{_CS}/HttpContentCompat.cs",
    f"{_CS}/Lock.cs",
    f"{_CS}/Services/CloudService/Lock.cs",
    f"{_CS}/Services/CloudService/MockCloudServiceClient.cs",
    f"{_CS}/Services/CloudService/MockCloudServiceClient.Accelerate.cs",
    f"{_CS}/Models/ICloudServiceSettings.cs",
)

# ==========================================================================
# 2b. wave-2: 依赖被移除类型(账号/令牌/Steam 本地读写)的次级模块
# ==========================================================================
DROP_FILES_2: tuple[str, ...] = (
    # ---- 仓储：只保留脚本仓储 ----
    f"{_ST}/Repositories/IGameAccountPlatformAuthenticatorRepository.cs",
    f"{_ST}/Repositories/IUserRepository.cs",
    f"{_ST}/Repositories/Implementation/GameAccountPlatformAuthenticatorRepository.cs",
    f"{_ST}/Repositories/Implementation/UserRepository.cs",
    f"{_ST}/Entities/GameAccountPlatformAuthenticator.cs",
    f"{_ST}/Entities/User.cs",
    # ---- 账号 / 令牌 / ASF / Steam 连接 的 MVVM 服务 ----
    f"{_ST}/Services/Mvvm/ASFService.cs",
    f"{_ST}/Services/Mvvm/AuthService.cs",
    f"{_ST}/Services/Mvvm/SteamConnectService.cs",
    f"{_ST}/Services/Mvvm/UserService.cs",
    # ---- Windows 平台：Steam 本地读写 / 压缩包 / 跳转列表 / 生物识别 ----
    "src/ST.Client.Desktop.Windows/Services/Implementation/SteamServiceImpl.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/SteamworksLocalApiServiceImpl.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/SevenZipHelper.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/JumpListServiceImpl.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/NativeWindowApiServiceImpl.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/PlatformBiometricServiceImpl.cs",
    "src/ST.Client.Desktop.Windows/Services/Implementation/WindowsProtectedData.cs",
    "src/ST.Client.Desktop.Windows/VdfHelper.cs",
    "src/ST.Client.Desktop.Windows/VisualStudioAppCenterSDK.cs",
    # ---- Avalonia：变更日志视图依赖已移除的 WebView3 ----
    f"{_AZ}/Application/UI/Views/Pages/About/About_ChangeLog.axaml",
    f"{_AZ}/Application/UI/Views/Pages/About/About_Changelog.axaml.cs",
    # ---- references 根目录下的 ASF / WebHost 适配文件 ----
    "references/ArchiSteamFarmLogManager.cs",
    "references/ArchiSteamFarmPathHelper.cs",
    "references/IArchiSteamFarmHelperService.cs",
    "references/WebHostExtensions.cs",
)

# ==========================================================================
# 3. .gitmodules entries to remove
# ==========================================================================
DROP_SUBMODULES: tuple[str, ...] = (
    "references/MetroRadiance",
    "references/SteamAchievementManager",
    "references/Steam4NET",
    "references/Depressurizer",
    "references/WinAuth",
    "references/ArchiSteamFarm",
    "references/dotnet-packaging",
    "references/SevenZipSharp",
)


def _rm(path: Path, dry: bool, actions: list[str]) -> None:
    if not path.exists():
        return
    rel = path.as_posix()
    if dry:
        actions.append(f"  [dry] rm {'dir ' if path.is_dir() else 'file'} {rel}")
        return
    if path.is_dir():
        shutil.rmtree(path, ignore_errors=True)
        actions.append(f"  rm dir  {rel}")
    else:
        try:
            path.unlink()
            actions.append(f"  rm file {rel}")
        except OSError as exc:
            actions.append(f"  !! failed {rel}: {exc}")


def prune_gitmodules(root: Path, dry: bool, actions: list[str]) -> None:
    gm = root / ".gitmodules"
    if not gm.exists():
        return
    lines = gm.read_text(encoding="utf-8").splitlines(keepends=True)
    out: list[str] = []
    i = 0
    removed = 0
    while i < len(lines):
        line = lines[i]
        if line.strip().startswith("[submodule") and i + 1 < len(lines):
            path_line = lines[i + 1]
            if path_line.strip().startswith("path"):
                mod_path = path_line.split("=", 1)[1].strip()
                if mod_path in DROP_SUBMODULES:
                    removed += 1
                    actions.append(f"  .gitmodules: -{mod_path}")
                    i += 2
                    while i < len(lines) and not lines[i].strip().startswith("["):
                        i += 1
                    continue
        out.append(line)
        i += 1
    if removed and not dry:
        gm.write_text("".join(out), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    root = Path(args.root).resolve()
    if not (root / "src").is_dir():
        print(f"not a repo root: {root}", file=sys.stderr)
        return 2

    actions: list[str] = []

    print("== Phase 1: 移除无关工程目录 ==")
    for d in DROP_DIRS:
        _rm(root / d, args.dry_run, actions)

    print("== Phase 2: 移除无关源文件 ==")
    for f in DROP_FILES:
        _rm(root / f, args.dry_run, actions)

    print("== Phase 2b: 移除次级依赖模块 ==")
    for f in DROP_FILES_2:
        _rm(root / f, args.dry_run, actions)

    print("== Phase 2c: 移除死文件 ==")
    for f in DROP_FILES_3:
        _rm(root / f, args.dry_run, actions)

    print("== Phase 3: 清理 .gitmodules ==")
    prune_gitmodules(root, args.dry_run, actions)

    if not args.dry_run:
        print("== Phase 4: 移除空目录 ==")
        # 注意: 不要清理 references/ —— 那里是子模块挂载点, 空目录是正常的
        for base in ("src", "tests"):
            for dirpath, dirnames, filenames in os.walk(root / base, topdown=False):
                p = Path(dirpath)
                try:
                    if p.is_dir() and not any(p.iterdir()):
                        p.rmdir()
                        actions.append(f"  rmdir   {p.relative_to(root).as_posix()}")
                except OSError:
                    pass

    print("\n== Manifest ==")
    for a in actions:
        print(a)
    print(f"\n共 {len(actions)} 项变更。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
