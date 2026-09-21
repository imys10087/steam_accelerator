using ReactiveUI;
using System.Application.Services;
using System.Application.Settings;
using System.Application.UI.Resx;
using System.Collections.Generic;
using System.Linq;
using System.Properties;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace System.Application.UI.ViewModels
{
    public partial class MainWindowViewModel : WindowViewModel
    {
        #region 更改通知

        bool mTopmost;
        public bool Topmost
        {
            get => mTopmost;
            set => this.RaiseAndSetIfChanged(ref mTopmost, value);
        }

        private ItemViewModel _SelectedItem;
        public ItemViewModel SelectedItem
        {
            get => _SelectedItem;
            set => this.RaiseAndSetIfChanged(ref _SelectedItem, value);
        }

        // 账号体系已随「仅保留 Steam/GitHub 加速」一并移除，
        // 用户菜单的开关状态（IsOpenUserMenu）与命令（OpenUserMenu）已删除。

        #endregion

        public CommunityProxyPageViewModel CommunityProxyPage => GetTabItemVM<CommunityProxyPageViewModel>();

        public ProxyScriptManagePageViewModel ProxyScriptPage => GetTabItemVM<ProxyScriptManagePageViewModel>();

        protected static readonly IPlatformService platformService = IPlatformService.Instance;
        public MainWindowViewModel()
        {
            if (IApplication.IsDesktopPlatform)
            {
                var adminTag = platformService.IsAdministrator ? (OperatingSystem2.IsWindows ? " (Administrator)" : " (Root)") : string.Empty;
                var title = $"{ThisAssembly.AssemblyTrademark} {RuntimeInformation.ProcessArchitecture.ToString().ToLower()} v{ThisAssembly.VersionDisplay} for {DeviceInfo2.OSName}{adminTag}";
#if DEBUG
                title = $"[Debug] {title}";
#endif
                Title = title;

            }

            #region InitTabItems

            // 只注册加速相关的两个页面：
            //   CommunityProxyPage     —— 加速项目开关与运行状态
            //   ProxyScriptManagePage  —— 代理脚本管理
            // 其余页面（起始页/账号/成就/令牌/挂卡/游戏工具/调试）已随功能模块移除。
            AddTabItem<CommunityProxyPageViewModel>();

            if (IApplication.IsDesktopPlatform)
            {
                AddTabItem<ProxyScriptManagePageViewModel>();
            }

            #endregion

            _SelectedItem = TabItems.First();

            R.Subscribe(() =>
            {
                foreach (var item in CurrentAllTabItems)
                {
                    item.RaisePropertyChanged(nameof(TabItemViewModelBase.Name));
                }
            }).AddTo(this);
        }

        public override void Initialize()
        {
            Task.Run(() =>
            {
                Threading.Thread.CurrentThread.IsBackground = true;
                if (!IsInitialized)
                {
                    Task.Run(async () =>
                    {
                        await ProxyService.Current.Initialize();
                        // ASF 挂卡（ASFSettings.AutoRunArchiSteamFarm → ASFService）
                        // 与 Steam 账号连接（SteamConnectService）已随对应模块移除。
                    });

                    Parallel.ForEach(TabItems, item =>
                    {
                        item.Initialize();
                        //Task.Run(item.Initialize).ForgetAndDispose();
                    });
                    IsInitialized = true;
                }
            }).ForgetAndDispose();
        }

        //public async override void Activation()
        //{
        //    if (IsFirstActivation)
        //    {
        //        if (UISettings.DoNotShowMessageBoxs.Value?.Contains(MessageBox.DontPromptType.Donate) == false)
        //        {
        //            //INotificationService.Instance.Notify("如果你觉得Steam++好用，你可以考虑给我们一些捐助以支持我们继续开发，谢谢！", NotificationType.Message);
        //            await MessageBox.ShowAsync("如果你觉得Steam++好用，你可以考虑给我们一些捐助以支持我们继续开发，谢谢！", button: MessageBox.Button.OK,
        //                rememberChooseKey: MessageBox.DontPromptType.Donate);
        //        }
        //    }
        //    base.Activation();
        //}
    }
}