using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using System.IO;
using System.Properties;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NInternalLogger = NLog.Common.InternalLogger;
using NLogLevel = NLog.LogLevel;
using NLogManager = NLog.LogManager;

namespace System.Application.UI
{
    partial interface IApplication
    {
        #region Logger

        const LogLevel DefaultLoggerMinLevel = ThisAssembly.Debuggable ? LogLevel.Debug : LogLevel.Error;

        public static readonly NLogLevel DefaultNLoggerMinLevel = ConvertLogLevel(DefaultLoggerMinLevel);

        /// <summary>
        /// Convert log level to NLog variant.
        /// </summary>
        /// <param name="logLevel">level to be converted.</param>
        /// <returns></returns>
        static NLogLevel ConvertLogLevel(LogLevel logLevel) => logLevel switch
        {
            // https://github.com/NLog/NLog.Extensions.Logging/blob/v1.7.0/src/NLog.Extensions.Logging/Logging/NLogLogger.cs#L416
            LogLevel.Trace => NLogLevel.Trace,
            LogLevel.Debug => NLogLevel.Debug,
            LogLevel.Information => NLogLevel.Info,
            LogLevel.Warning => NLogLevel.Warn,
            LogLevel.Error => NLogLevel.Error,
            LogLevel.Critical => NLogLevel.Fatal,
            LogLevel.None => NLogLevel.Off,
            _ => NLogLevel.Debug,
        };

        static void SetNLoggerMinLevel(LogLevel logLevel) => SetNLoggerMinLevel(ConvertLogLevel(logLevel));

        static void SetNLoggerMinLevel(NLogLevel logLevel)
        {
            NLogManager.GlobalThreshold = logLevel;
            NInternalLogger.LogLevel = logLevel;
        }

        public static void TrySetLoggerMinLevel(LogLevel logLevel)
        {
            try
            {
                var o = LoggerFilterOptions;
                if (o != null) o.MinLevel = logLevel;
            }
            catch
            {
            }
            SetNLoggerMinLevel(ConvertLogLevel(logLevel));
        }

        static LoggerFilterOptions? _LoggerFilterOptions;

        /// <summary>
        /// 日志过滤选项
        /// </summary>
        static LoggerFilterOptions? LoggerFilterOptions
        {
            get
            {
                if (_LoggerFilterOptions != null) return _LoggerFilterOptions;
                return DI.Get_Nullable<IOptions<LoggerFilterOptions>>()?.Value;
            }
            set
            {
                _LoggerFilterOptions = value;
            }
        }

        /// <summary>
        /// 配置 NLog 提供程序，仅初始化时调用
        /// </summary>
        /// <returns></returns>
        public static (LogLevel minLevel, Action<ILoggingBuilder> cfg) ConfigureLogging()
        {
            var minLevel = DefaultLoggerMinLevel;

            void _(ILoggingBuilder builder)
            {
                builder.ClearProviders();

                builder.SetMinimumLevel(minLevel);
                SetNLoggerMinLevel(minLevel);

                // 可以多个日志提供同时用，比如还可以在 Win 平台再添加一个 Windows 事件日志

                builder.AddNLog(NLogManager.Configuration); // 添加 NLog 日志
            }

            return (minLevel, (Action<ILoggingBuilder>)_);
        }

        /// <summary>
        /// 日志最低等级
        /// </summary>
        public static LogLevel LoggerMinLevel
        {
            get
            {
                var o = LoggerFilterOptions;
                if (o == null)
                {
                    o = new();
                    LoggerFilterOptions = o;
                }
                return o.MinLevel;
            }
            set
            {
                var o = LoggerFilterOptions;
                if (o != null)
                {
                    o.MinLevel = value;
                }
                SetNLoggerMinLevel(value);
            }
        }

        /// <summary>
        /// 启用日志
        /// </summary>
        public static bool EnableLogger
        {
            get => LoggerMinLevel > LogLevel.None;
            set
            {
                LoggerMinLevel = value ? DefaultLoggerMinLevel : LogLevel.None;
            }
        }

        #endregion

        #region LogHelper

        /// <summary>
        /// 日志存放文件夹路径
        /// </summary>
        public static string LogDirPath { get; private set; } = string.Empty;

#if DEBUG
        /// <summary>
        /// 日志文件夹是否存放在缓存文件夹中，通常日志文件夹将存放在基目录上，因某些平台基目录为只读，则只能放在缓存文件夹中
        /// </summary>
        [Obsolete("only True.", true)]
        public static bool LogUnderCache => true;
#endif

        public const string LogDirName = "Logs";

        /// <summary>
        /// 初始化日志目录与 NLog 配置。
        ///
        /// <para><b>裁剪说明</b></para>
        /// <para>
        /// 原实现额外配置了一份 ArchiSteamFarm 专用日志（独立的日志目录、FileTarget 与
        /// LoggingRule，并通过 <c>IArchiSteamFarmService.InitCoreLoggers</c> 接管 ASF 的
        /// 全局 LogManager）。ASF 挂卡模块已移除，这些配置随之删除，
        /// 日志输出只保留本工具自身的 <c>nlog-all-*.log</c>。
        /// </para>
        /// </summary>
        public static void InitLogDir(string? alias = null)
        {
            if (!string.IsNullOrEmpty(LogDirPath)) return;

            var logDirPath = Path.Combine(IOPath.CacheDirectory, LogDirName);
            IOPath.DirCreateByNotExists(logDirPath);
#if StartupTrace
            StartupTrace.Restart("InitLogDir.IO");
#endif
            var logDirPath_ = logDirPath + Path.DirectorySeparatorChar;

            NInternalLogger.LogFile = logDirPath_ + "internal-nlog" + alias + ".txt";
            NInternalLogger.LogLevel = NLogLevel.Error;

            var objConfig = new LoggingConfiguration();
            var defMinLevel = DefaultNLoggerMinLevel;

            var logfile = new FileTarget("logfile")
            {
                FileName = logDirPath_ + "nlog-all-${shortdate}" + alias + ".log",
                Layout = "${longdate}|${level}|${logger}|${message} |${all-event-properties} ${exception:format=tostring}",
                ArchiveAboveSize = 10485760,
                MaxArchiveFiles = 14,
                MaxArchiveDays = 7,
            };

            objConfig.AddTarget(logfile);

            objConfig.AddRule(NLogLevel.Error, NLogLevel.Fatal, logfile, "Microsoft.*");
            objConfig.AddRule(NLogLevel.Error, NLogLevel.Fatal, logfile, "System.Net.Http.*");
            objConfig.AddRule(defMinLevel, NLogLevel.Fatal, logfile, "*");
#if StartupTrace
            StartupTrace.Restart("InitLogDir.CreateLoggingConfiguration");
#endif
            NLogManager.Configuration = objConfig;

            LogDirPath = logDirPath;
        }

        #endregion
    }
}
