using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.I18n;

namespace ViewPrism2.Tests;

/// <summary>
/// テスト用 LocalizationService。ECO-079 以降、画像/作業タブの文言は XAML 直書きから Loc[key] バインドへ
/// 移行したため、View を描画して文言を検査するテストは実アセット(ja 既定)で解決する必要がある。
/// VM 構築の共通ヘルパーは <see cref="Ja"/>(実 Assets/i18n を読み込む)を用いる。
/// </summary>
internal static class TestLoc
{
    /// <summary>実 Assets/i18n を読み込んだ loc(既定 ja)。View 描画+文言検査のあるテストはこれを使う。</summary>
    public static LocalizationService Ja() =>
        new(I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");
}
