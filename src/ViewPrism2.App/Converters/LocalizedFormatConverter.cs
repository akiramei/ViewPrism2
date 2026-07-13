using System.Globalization;
using Avalonia.Data.Converters;

namespace ViewPrism2.App.Converters;

/// <summary>
/// ローカライズ済みテンプレート(string.Format 形式 {0})に実行時の値を差し込む多値コンバータ(ECO-079)。
/// 第1値=Loc[key] で解決した書式テンプレート、第2値以降=差し込む値。XAML の StringFormat 直書き
/// (K-AVALONIA 違反=言語切替非追随)を置き換え、テンプレート自体も言語切替へ追随させる。
/// <see cref="Instance"/> の単一実体を <c>{x:Static}</c> で参照する(リソース登録不要)。
/// </summary>
public sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public static readonly LocalizedFormatConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not string template)
        {
            return string.Empty;
        }

        var args = values.Skip(1).Select(v => v ?? string.Empty).ToArray();
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template; // テンプレート不正時は素の書式を返す(例外を投げない=REQ-050 の精神)
        }
    }
}
