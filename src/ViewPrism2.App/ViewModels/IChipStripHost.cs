namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 固定クローム チップ行の共有部品(LabeledChipStrip)が要求するホスト契約(ECO-094)。
/// 画像タブ/作業タブは同一契約の 2 面(IMG-023=VC-IMG-9/10=VC-WORK-2/3・ECO-091)—
/// ECO-090 の暫定統制(2 面コピペ+read-across 必須)を部品共有で構造的に置換する。
/// 通知は実装 VM の INotifyPropertyChanged に乗る(本契約は読み取り面のみ)。
/// </summary>
public interface IChipStripHost
{
    /// <summary>折畳み・overflow の意味論(単一実装・ECO-091)。</summary>
    ChipStripViewModel ChipStrip { get; }

    bool ShowChips { get; }
    bool ShowChipHint { get; }
    string ChipHintLabel { get; }

    /// <summary>ポップオーバー検索欄 placeholder・空表示の文言解決(REQ-050/051=直書き禁止)。</summary>
    LocalizationProxy Loc { get; }

    /// <summary>通常領域チップの直接クリック(ジェスチャ起点は View の direct ハンドラ経由=ECO-087 教訓)。</summary>
    void ClickChip(ChipVM chip);
}
