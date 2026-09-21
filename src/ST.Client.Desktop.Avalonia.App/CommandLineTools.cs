using System.Application.Services;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace System.Application.UI
{
    partial class Program
    {
        const string command_main = "main";
        static IApplication.SingletonInstance? appInstance;

        /// <summary>
        /// 命令行工具(Command Line Tools/CLT)
        /// </summary>
        static class CommandLineTools
        {
            public static int Run(string[] args)
            {
                if (args.Length == 0) args = new[] { "-h" };

                // https://docs.microsoft.com/zh-cn/archive/msdn-magazine/2019/march/net-parse-the-command-line-with-system-commandline
                var rootCommand = new RootCommand("命令行工具(Command Line Tools/CLT)");

                void MainHandler() => MainHandler_(null);
                void MainHandler_(Action? onInitStartuped)
                {
#if StartupTrace
                    StartupTrace.Restart("ProcessCheck");
#endif
                    Startup.Init(IsMainProcess ? DILevel.MainProcess : DILevel.Min);
#if StartupTrace
                    StartupTrace.Restart("Startup.Init");
#endif
                    onInitStartuped?.Invoke();
                    if (IsMainProcess)
                    {
                        var isInitAppInstanceReset = false;
                    initAppInstance: appInstance = new();
                        if (!appInstance.IsFirst)
                        {
                            //Console.WriteLine("ApplicationInstance.SendMessage(string.Empty);");
                            if (IApplication.SingletonInstance.SendMessage(string.Empty))
                            {
                                return;
                            }
                            else
                            {
                                if (!isInitAppInstanceReset &&
                                    IApplication.SingletonInstance.TryKillCurrentAllProcess())
                                {
                                    isInitAppInstanceReset = true;
                                    appInstance.Dispose();
                                    goto initAppInstance;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                        appInstance.MessageReceived += value =>
                        {
                            if (string.IsNullOrEmpty(value))
                            {
                                var app = App.Instance;
                                if (app != null)
                                {
                                    MainThread2.BeginInvokeOnMainThread(app.RestoreMainWindow);
                                }
                            }
                        };
                    }
                    //#if StartupTrace
                    //                    StartupTrace.Restart("ApplicationInstance");
                    //#endif
                    //                    initCef();
                    //#if StartupTrace
                    //                    StartupTrace.Restart("InitCefNetApp");
                    //#endif
                    if (IsMainProcess)
                    {
                        BuildAvaloniaAppAndStartWithClassicDesktopLifetime(args);
                    }
#if StartupTrace
                    StartupTrace.Restart("InitAvaloniaApp");
#endif
                }
                void MainHandlerByCLT() => MainHandlerByCLT_(null);
                void MainHandlerByCLT_(Action? onInitStartuped)
                {
                    IsMainProcess = true;
                    IsCLTProcess = false;
                    MainHandler_(onInitStartuped);
                }

#if DEBUG
                // -clt debug -args 730
                var debug = new Command("debug", "调试");
                debug.AddOption(new Option<string>("-args", () => "", "测试参数"));
                debug.Handler = CommandHandler.Create((string args) => // 参数名与类型要与 Option 中一致！
                {
                    //Console.WriteLine("-clt debug -args " + args);
                    // OutputType WinExe 导致控制台输入不会显示，只能附加一个新的控制台窗口显示内容，不合适
                    // 如果能取消 管理员权限要求，改为运行时管理员权限，
                    // 则可尝试通过 Windows Terminal 或直接 Host 进行命令行模式
                    MainHandlerByCLT();
                });
                rootCommand.AddCommand(debug);
#endif

                var main = new Command(command_main)
                {
                    Handler = CommandHandler.Create(MainHandler)
                };
                rootCommand.AddCommand(main);

                // -clt devtools
                // -clt devtools -disable_gpu
                // -clt devtools -use_wgl
                var devtools = new Command("devtools");
                devtools.AddOption(new Option<bool>("-disable_gpu", () => false, "禁用 GPU 硬件加速"));
                devtools.AddOption(new Option<bool>("-use_wgl", () => false, "使用 Native OpenGL(仅 Windows)"));
                devtools.Handler = CommandHandler.Create((bool disable_gpu, bool use_wgl) =>
                {
                    IApplication.EnableDevtools = true;
                    IApplication.DisableGPU = disable_gpu;
                    IApplication.UseWgl = use_wgl;
                    MainHandlerByCLT_(onInitStartuped: () =>
                    {
                        IApplication.LoggerMinLevel = LogLevel.Debug;
                    });
                });
                rootCommand.AddCommand(devtools);

                // -clt c -silence
                var common = new Command("c", "common");
                common.AddOption(new Option<bool>("-silence", "静默启动（不弹窗口）"));
                common.Handler = CommandHandler.Create((bool silence) =>
                {
                    IsMinimize = silence;
                    MainHandlerByCLT();
                });
                rootCommand.AddCommand(common);

                // 已移除 `-clt steam -account` 子命令：Steam 账号切换（ISteamService）随账号模块裁剪。

                // 已移除 `-clt app -id` 子命令：成就解锁（SteamConnectService / IViewModelManager.InitUnlockAchievement）
                // 随成就模块裁剪。

                var r = rootCommand.InvokeAsync(args).Result;
                return r;
            }
        }
    }
}