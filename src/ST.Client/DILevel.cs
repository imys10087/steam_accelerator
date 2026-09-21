using System.Net.Http;

namespace System.Application
{
    /// <summary>
    /// DI 服务级别。用于按进程用途（主进程 / 子进程 / 命令行工具）装配不同规模的服务集合。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原枚举含 <c>Steam = 64</c> 一级，用于装配 Steam 本地读写、成就、本地令牌与 ASF 挂卡等服务。
    /// 这些模块已全部移除，对应的标志位与 <c>MainProcess</c> 组合中的引用一并删除。
    /// </para>
    /// </summary>
    [Flags]
    public enum DILevel
    {
        /// <summary>最小集合。</summary>
        Min = 0,

        /// <summary>服务端 API + Repositories + Storage + ModelValidator。</summary>
        ServerApiClient = 2,

        /// <summary>图形界面。</summary>
        GUI = 4,

        /// <summary><see cref="IHttpClientFactory"/> 服务。</summary>
        HttpClientFactory = 8,

        /// <summary>Hosts 文件。</summary>
        Hosts = 16,

        /// <summary>AppUpdate + 托盘图标（影响主窗口关闭与退出模式，仅在主进程中才会显示托盘）。</summary>
        MainProcessRequired = 32,

        /// <summary>Http 代理（加速核心）。</summary>
        HttpProxy = 128,

        /// <summary>主进程所需级别组合；仅用于指定 DI 等级，当前进程不一定为主进程。</summary>
        MainProcess =
            ServerApiClient |
            GUI |
            HttpClientFactory |
            Hosts |
            MainProcessRequired |
            HttpProxy,
    }
}
