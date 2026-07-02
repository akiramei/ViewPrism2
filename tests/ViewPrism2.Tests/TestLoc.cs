using ViewPrism2.Core.Services;

namespace ViewPrism2.Tests;

/// <summary>
/// テスト用の最小 LocalizationService。ローカライズを検査しないテスト(ImageTabViewModel 構築など)向けに、
/// 空リソースの loc を1つ用意する(欠落キーはキー文字列を返すため挙動検査には影響しない)。
/// </summary>
internal static class TestLoc
{
    public static LocalizationService Empty() =>
        new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
}
