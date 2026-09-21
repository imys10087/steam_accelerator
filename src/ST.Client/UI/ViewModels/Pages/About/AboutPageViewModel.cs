using ReactiveUI;
using System.Properties;
using System.Reactive;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace System.Application.UI.ViewModels
{
    /// <summary>
    /// 「关于」页面。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <list type="bullet">
    /// <item>移除捐赠排行（<c>DonateList</c> / <c>DonateFliterDate</c> / <c>LoadDonateRankListData</c>）：
    ///       属运营侧功能，依赖已删除的 <c>ICloudServiceClient.DonateRanking</c> 与 Ranking DTO；</item>
    /// <item>移除账号相关命令（<c>DelAccountCommand</c>、<c>UIDCommand</c>）以及移动端的
    ///       <c>PreferenceButton</c> 组装逻辑：依赖已删除的 <c>UserService</c> / <c>IUserManager</c>；</item>
    /// <item>移除 <c>CheckUpdateCommand</c>：应用更新服务已随更新模块移除；</item>
    /// <item>移除 <c>RmbadminSteamLink</c>：依赖已删除的 <c>SteamApiUrls</c>。</item>
    /// </list>
    /// <para>余下的 <c>OpenBrowserCommand</c> 与各链接常量供「关于」页面展示使用。</para>
    /// </summary>
    public partial class AboutPageViewModel
    {
        public static AboutPageViewModel Instance { get; } = new();

        public ReactiveCommand<string, Unit> OpenBrowserCommand { get; }

        public AboutPageViewModel()
        {
            IconKey = nameof(AboutPageViewModel);

            OpenBrowserCommand = ReactiveCommand.CreateFromTask<string>(Browser2.OpenAsync);
        }

        public string VersionDisplay => $"{ThisAssembly.VersionDisplay} for {DeviceInfo2.OSName} ({RuntimeInformation.ProcessArchitecture.ToString().ToLower()})";

        public string LabelVersionDisplay => ThisAssembly.IsAlphaRelease ? "Alpha Version:" : (ThisAssembly.IsBetaRelease ? "Beta Version:" : "Current Version:");

        public static string Copyright
        {
            get
            {
                // https://www.w3cschool.cn/html/html-copyright.html
                int startYear = 2020, thisYear = 2021;
                var nowYear = DateTime.Now.Year;
                if (nowYear < thisYear) nowYear = thisYear;
                return $"© {startYear}{(nowYear == startYear ? startYear : "-" + nowYear)} {ThisAssembly.AssemblyCompany}. All Rights Reserved.";
            }
        }

        public const string Zhengye = "Zhengye";
        public const string 沙中金 = "沙中金";
        public const string EspRoy = "EspRoy";

        #region Urls

        public static string RmbadminLink => UrlConstants.GitHub_User_Rmbadmin;

        public static string AigioLLink => UrlConstants.GitHub_User_AigioL;

        public static string MossimosLink => UrlConstants.GitHub_User_Mossimos;

        public static string CliencerLink => UrlConstants.BILI_User_Cliencer;

        public static string PrivacyLink => UrlConstants.OfficialWebsite_Privacy;

        public static string AgreementLink => UrlConstants.OfficialWebsite_Agreement;

        public static string OfficialLink => UrlConstants.OfficialWebsite;

        public static string SourceCodeLink => UrlConstants.GitHub_Repository;

        public static string UserSupportLink => UrlConstants.OfficialWebsite_Contact;

        public static string BugReportLink => UrlConstants.GitHub_Issues;

        public static string FAQLink => UrlConstants.OfficialWebsite_Faq;

        public static string ChangeLogLink => UrlConstants.OfficialWebsite_Changelog;

        public static string LicenseLink => UrlConstants.License_GPLv3;

        public static string MicrosoftStoreReviewLink => UrlConstants.MicrosoftStoreReviewLink;

        #endregion
    }
}
