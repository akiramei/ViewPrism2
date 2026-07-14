namespace ViewPrism2.Core.Models;

/// <summary>画像ステータス(仕様 §2.1 REQ-016)。Deleted は V1 では予約値。</summary>
public enum ImageStatus
{
    Normal,
    Missing,
    Deleted,
    Pending,
}

/// <summary>タグの 3 種意味論(仕様 §2.2 REQ-020)。</summary>
public enum TagType
{
    Simple,
    Textual,
    Numeric,
}

/// <summary>ビュー条件の演算子(仕様 §2.3 REQ-031)。</summary>
public enum ConditionOperator
{
    Exists,
    Equals,
    Between,
    Regexp,
    In,
}

/// <summary>整列フィールド(仕様 §2.3 REQ-038)。既定 Name。</summary>
public enum SortField
{
    Name,
    CreatedDate,
    ModifiedDate,
    FileSize,
}

/// <summary>整列方向(仕様 §2.3 REQ-038)。既定 Asc。</summary>
public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>階層ノードの値条件種別(仕様 §2.4 REQ-034)。null=条件なし。</summary>
public enum HierarchyConditionType
{
    Equals,
    Range,
    Pattern,
    Values,
}

/// <summary>
/// textual タグの候補値リストの意味(仕様 §2.2 REQ-095 / ECO-086 裁定 b)。既定 Suggest。
/// Closed でも付与は拒否しない(リスト外の付与値は保持され、定義値展開で未定義値として検出される)。
/// </summary>
public enum TagValueDomain
{
    /// <summary>入力補助(REQ-024 の現行意味論)。</summary>
    Suggest,
    /// <summary>閉じた値集合(定義済みリストを値の正とする)。</summary>
    Closed,
}

/// <summary>
/// ビュー階層ノードの展開モード(仕様 §2.4 REQ-096 / ECO-086 裁定 a)。既定 Observed。
/// DB 列 NULL は Observed として読む(既存ビュー完全互換)。
/// </summary>
public enum HierarchyExpansionMode
{
    /// <summary>値ノードを自動生成しない(タグ名ノードのみ)。</summary>
    Manual,
    /// <summary>観測値展開=REQ-035 の現行挙動(既定)。</summary>
    Observed,
    /// <summary>定義値展開: textual=predefined_values 定義順 / numeric=min..max step 刻み。0 件でも生成。</summary>
    Defined,
    /// <summary>定義値を定義順で先に、定義にない付与値を末尾に序数昇順で追加(裁定 f)。</summary>
    DefinedAndObserved,
}

/// <summary>NodeGraph ノードの種別(仕様 §2.4 REQ-035)。</summary>
public enum NodeKind
{
    /// <summary>ルート(無条件)。</summary>
    Root,
    /// <summary>タグ名ノード(simple/numeric タグ、または値 0 件の textual タグ)。</summary>
    TagName,
    /// <summary>textual タグの値ノード(distinct 値 2 件以上)。</summary>
    Value,
    /// <summary>一体型ノード「タグ名: 値」(distinct 値 1 件)。</summary>
    Combined,
}

/// <summary>ビューア表示モード(仕様 §2.9 REQ-054)。既定 Normal。</summary>
public enum ViewerMode
{
    /// <summary>単一画像表示(§2.6 REQ-044。V1 表示)。</summary>
    Normal,
    /// <summary>縦連続スクロール(REQ-055)。</summary>
    Scroll,
    /// <summary>右開き見開き(日本式。右=現在 index・左=現在+1)(REQ-056)。</summary>
    SpreadRight,
    /// <summary>左開き見開き(左=現在 index・右=現在+1)(REQ-056)。</summary>
    SpreadLeft,
}

/// <summary>見開きの開き方向(仕様 §2.9 REQ-056、OC-9)。</summary>
public enum SpreadDirection
{
    /// <summary>右開き(日本式)。右=現在 index・左=現在+1。</summary>
    Right,
    /// <summary>左開き。左=現在 index・右=現在+1。</summary>
    Left,
}

/// <summary>見開きの高さ統一方式(仕様 §2.9 REQ-058、OC-12)。既定 NoResize。</summary>
public enum ResizeMode
{
    /// <summary>ペアの高い方の画像高さへ統一(viewport*0.9 上限)。</summary>
    MatchLargerHeight,
    /// <summary>ペアの低い方の画像高さへ統一(viewport*0.9 上限)。</summary>
    MatchSmallerHeight,
    /// <summary>原寸(統一高さ概念なし。領域超過時のみ領域内に収める=上限 100%)。</summary>
    NoResize,
}

/// <summary>見開きの垂直揃え(仕様 §2.9 REQ-058)。既定 Middle。スクロールは対象外。</summary>
public enum AlignMode
{
    Top,
    Middle,
    Bottom,
}

/// <summary>画像間ギャップ方式(仕様 §2.9 REQ-058)。既定 Tight。</summary>
public enum GapMode
{
    /// <summary>間隔 0(見開きでは左右画像を中央境界へ寄せる)。</summary>
    Tight,
    /// <summary>customGapPx の間隔。</summary>
    Loose,
}

/// <summary>ページ送りモード(仕様 §2.9 REQ-057)。既定 DoublePage(step=2)。</summary>
public enum PageTurnMode
{
    /// <summary>2 ページ送り(step=2)。</summary>
    DoublePage,
    /// <summary>1 ページ送り(step=1)。</summary>
    SinglePage,
}

/// <summary>単一(normal)モードの画像フィット方式(モック準拠の改善)。既定 Fit(画面に合わせる=縮小のみ)。</summary>
public enum FitMode
{
    /// <summary>画面に合わせる(Uniform・縮小のみ・現行挙動)。</summary>
    Fit,
    /// <summary>幅に合わせる(横幅基準で拡縮・縦はスクロール)。</summary>
    Width,
    /// <summary>原寸 1:1(拡縮なし・はみ出しはスクロール)。</summary>
    One,
}

/// <summary>キャンバス下地色(モック準拠の改善)。既定 Dark。全モード共通。</summary>
public enum BackgroundMode
{
    /// <summary>ダーク(#15171c)。</summary>
    Dark,
    /// <summary>ライト(淡色)。</summary>
    Light,
    /// <summary>市松(チェッカー。透過確認向け)。</summary>
    Checker,
}

/// <summary>縦スクロールモードの横揃え(モック準拠の改善)。幅が異なる画像の寄せ方。既定 Center。</summary>
public enum ScrollHAlign
{
    Left,
    Center,
    Right,
}
