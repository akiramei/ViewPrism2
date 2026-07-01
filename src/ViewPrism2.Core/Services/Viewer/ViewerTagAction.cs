namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// タグ制御モードの予約アクション 6 種(仕様 §2.12.1・OC-23。画面の左右ページ基準)。
/// ECO-022 新設。純粋計算 Core(M-TAGCTRL-028)。
/// </summary>
/// <remarks>
/// 列挙値そのものは支配順を表さない。競合解決の全順序
/// <c>skip &gt; spread &gt; forceLeftPage &gt; forceRightPage &gt; leftPageEmpty &gt; rightPageEmpty</c>
/// は <see cref="TagActionResolver"/> が定める(仕様 §2.12.1・TC-3)。
/// </remarks>
public enum ViewerTagAction
{
    /// <summary>見開きで画面左ページにのみ配置(必要なら手前に空白を挿入して側を合わせる)。</summary>
    ForceLeftPage,

    /// <summary>見開きで画面右ページにのみ配置(同上)。</summary>
    ForceRightPage,

    /// <summary>1 枚で見開き全体(左右 2 ページ分)を占有。</summary>
    Spread,

    /// <summary>表示対象から除外(列から除去)。</summary>
    Skip,

    /// <summary>その画像の見開きの画面左を空白にし、画像を画面右へ。</summary>
    LeftPageEmpty,

    /// <summary>その画像の見開きの画面右を空白にし、画像を画面左へ。</summary>
    RightPageEmpty,
}
