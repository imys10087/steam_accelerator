using System.Application;
using System.Application.UI;

namespace System.Application.UI
{
    /// <summary>
    /// 资源图标
    /// </summary>
    public enum ResIcon
    {
        None,
        AvatarDefault,
        AccountBox,
        Info,
        Person,
        Settings,
        SportsEsports,
        VerifiedUser,
        Steam,
        Xbox,
        Apple,
        QQ,
        Phone,
        /// <summary>
        /// 根据当前平台使用平台对应的📱图标，目前支持材料设计中 Android Phone 与 iPhone
        /// </summary>
        PlatformPhone,
    }
}

// 已移除 ResIconEnumExtensions（FastLoginChannel → ResIcon 映射）：
// 账号快速登录渠道随账号模块一并删除，该扩展已无使用者。