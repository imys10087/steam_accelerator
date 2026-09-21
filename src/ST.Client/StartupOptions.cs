namespace System.Application
{
    /// <summary>
    /// 由 <see cref="DILevel"/> 展开出的服务装配开关集合。
    ///
    /// <para>裁剪后不再包含 <c>HasSteam</c>（Steam 本地读写 / 成就 / 令牌 / ASF 服务组已移除）。</para>
    /// </summary>
    public sealed class StartupOptions
    {
        public bool HasMainProcessRequired { get; set; }

        public bool HasNotifyIcon { get; set; }

        public bool HasGUI { get; set; }

        public bool HasServerApiClient { get; set; }

        public bool HasHttpClientFactory { get; set; }

        public bool HasHttpProxy { get; set; }

        public bool HasHosts { get; set; }

        public StartupOptions(DILevel level)
        {
            mValue = this;

            HasMainProcessRequired = level.HasFlag(DILevel.MainProcessRequired);
            HasNotifyIcon = HasMainProcessRequired;
            HasGUI = level.HasFlag(DILevel.GUI);
            HasServerApiClient = level.HasFlag(DILevel.ServerApiClient);
            HasHttpClientFactory = level.HasFlag(DILevel.HttpClientFactory);
            HasHttpProxy = level.HasFlag(DILevel.HttpProxy);
            HasHosts = level.HasFlag(DILevel.Hosts);
        }

        static StartupOptions? mValue;

        public static StartupOptions Value => mValue ?? throw new NullReferenceException("StartupOptions init fail.");
    }
}
