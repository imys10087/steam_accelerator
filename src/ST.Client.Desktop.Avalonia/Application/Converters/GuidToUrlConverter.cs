using System.Globalization;

namespace System.Application.Converters
{
    /// <summary>
    /// 把 ImageId（<see cref="Guid"/>）转换为图片 URL。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原实现调用 <c>ImageUrlHelper.GetImageApiUrlById</c> 拼出服务端图片接口地址
    /// （<c>{ApiBaseUrl}/api/image/{id}</c>）。该图片通道随「加速项目图标」一并裁剪，
    /// 因此本转换器不再产出 URL，只返回 <see langword="null"/>
    /// ——与原实现遇到 <see cref="Guid.Empty"/> 时的行为一致。
    /// 保留类型本身是为了不破坏引用它的 XAML 资源声明；
    /// 若后续恢复服务端图片，只需在此重新拼装 URL。
    /// </para>
    /// </summary>
    public class GuidToUrlConverter : ImageValueConverter
    {
        public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => null;
    }
}
