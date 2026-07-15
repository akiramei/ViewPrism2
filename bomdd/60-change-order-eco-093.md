# Change Order — ECO-093(タグ編集ダイアログ プレビュー帯の候補値多量時の乱れ — 溢れ非封じ込め+多量時表示の CAD 未定義)

- 起票日: 2026-07-15
- 報告者: maintainer(ECO-092 golden 中の実機所見・R3 分離起票=別サーフェス)
- 種別: 不具合(溢れの非封じ込め=実装欠陥)+CAD 未定義(多量時のプレビュー表示設計)の複合
- status: staged

## 1. 症状

タグ編集ダイアログの**付与プレビュー帯**(下端固定・ECO-007/E5=DC-TAGPREVIEW-001)で、
テキスト型の候補値が多量(47 件級=都道府県)のとき、候補値チップの**末尾が右ドックの説明文
(「画像に付けたときの見え方」)と重なって描画**され判読不能になる(maintainer 実機
スクリーンショット: 「茨城県」チップに説明文の「けたと」が重なる)。

## 2. 工程診断(R2)

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD(ViewPrismUI) | **部分未定義** | tag_tab.md レイアウト不変条件は「下部ライブプレビューは固定(候補値が増えてもフォームスクロールに埋もれない)」=帯の存在・固定性のみ定義。**帯の中で候補値が多量のときの表示(件数・省略)は mock 未定義**(mock デモは少数件)。ただし「他要素と重なって判読不能」は設計以前の封じ込め欠陥 |
| BOM | 谷間 | CP-TAGDLG-087(ダイアログ面)にプレビュー帯の多量候補値次元なし(fixture は少数件=ECO-088 教訓 2 の同族) |
| 実装 | **欠陥と確定(封じ込め)** | [TagEditorWindow.axaml:180-198](../src/ViewPrism2.App/Views/TagEditorWindow.axaml#L180): PreviewChips を**横 StackPanel(折返しなし・幅制限なし)**で列挙。DockPanel の中央領域を溢れ、右ドックの caption と重なる。[TagEditorViewModel.cs:211](../src/ViewPrism2.App/ViewModels/TagEditorViewModel.cs#L211): PreviewChips は**全候補値**を列挙(件数制限なし) |

- 混入: ECO-007/E5(プレビュー帯初版)から。潜伏機序= ECO-088 族と同一(候補値が少数の間は
  1 行に収まり視覚に現れない機能等価潜伏)。47 件級の実データ(ECO-092 golden)が初の可視化条件。
- ECO-092 との関係: **別サーフェス**(パレットカード vs 編集ダイアログのプレビュー帯)。
  ECO-092 の diff は本面に触れていない(TagsTabView/TagPaletteViewModel のみ)。
- WrapPanel 全数走査(ECO-089)との関係: 本面は WrapPanel でなく「折返し意図のない横 StackPanel」
  のため走査対象外だった — 「溢れの封じ込め」全般は走査の検査次元になかった(検査の谷間)。

## 3. 切り分け済みの事実(確定/未検証の分離)

確定:

1. PreviewChips=全候補値(47 件)を単一行で描画(§2)。クリップなし=右ドック caption と重なる。
2. プレビュー帯の固定性(docked・スクロールに埋もれない)は健全(GF-086-01 系の probe 緑)。
3. TAG-013=T-a(2026-07-15)はパレットカードの裁定であり本面を直接拘束しないが、
   「プレビュー面=要約+非対話」の様式は既裁定の類推として利用可能。

疑い(未検証):

- (a) 少数件時の現行視覚(先頭=選択強調チップ)は golden G-6/ECO-087 承認済み — 是正は
  多量時のみ変化し少数件は不変にできる見込み(fix プローブで確認)。

## 4. 是正方針(案・gate① で裁定)

| 案 | 内容 | diff 規模 | 含意 |
|---|---|---|---|
| A 封じ込めのみ | チップ列をクリップ(帯内で切る)し重なりを解消。件数は全件のまま | 極小(コンテナ 1 箇所) | 判読不能は解消するが「どこまであるか」は見えない(見切れ)。mock 沈黙への最小介入 |
| **B TAG-013 類推の要約** | プレビュー帯の候補値を**先頭 k 件+非対話「ほか N 件」**へ(パレット T-a と同様式・ChipRowOverflow/chip.moreItems 再利用)。単一行なので k=幅実測 or 固定数(mock デモ規模=3〜5 件) | 小(VM 表示派生+XAML) | プレビュー=要約の一貫様式(TAG-013 類推)。ただし mock 未定義への設計判断を含む → **裁定が必要** |

- 推奨: **B**(固定数 k=3〜4 の単純形)— プレビューの意味(見え方の例示)には全件列挙は不要で、
  パレット T-a と同じ「要約+非対話 ほか N 件」が UI 言語として一貫する。
  採択時は CAD(tag_tab.md ライブプレビュー節+mock)へ先に反映(CAD 是正が先・製品コードは後)。

## 5. 影響 BOM

- 実装: TagEditorWindow.axaml(プレビュー帯チップ列)+TagEditorViewModel(表示派生・案 B の場合)
- テスト: プレビュー帯の封じ込め probe(右ドック caption と非交差・是正前赤)+少数件不変 pin
- CAD: 案 B なら tag_tab.md ライブプレビュー節+タグ作成ダイアログ mock へ多量時表示を追記(裁定後)
- CP: CP-TAGDLG-087 へ多量候補値次元を追加
- i18n: 案 B は chip.moreItems 再利用(新規なし)

## 6. 残ゲート

- **gate①(裁定): 必要** — 案 A(封じ込めのみ)/案 B(先頭 k 件+非対話ほか N 件・推奨)。
  案 B の場合は k の値(固定 3〜4 か幅実測か)も併せて。
- gate②(golden): 是正後に実機承認(多量時の乱れ解消+少数件の視覚不変=G-6/ECO-087 承認済み視覚)。

## 7. 裁定記録(2026-07-15 maintainer)

**gate① 裁定=案 B 採択**(先頭 k 件+非対話「ほか N 件」)。k は推奨の単純形= **固定 3 件**で
CAD 正典化(mock のデモ規模と一致・実装も固定数で単純)。

**CAD 先行反映済み(VPUI `afc8878`)**: tag_tab.md ライブプレビュー節へ多量時仕様+**VC-TAG-11 新設**
(先頭 3 件+非対話ほか N 件・単一行維持・右ドック説明文と重ならない・少数件〈3 件以下〉の視覚不変)。
mock(タグ作成ダイアログ)の previewOptions を先頭 3 件+非対話ほか N 件へ改版(PREVIEW_MAX=3)。

**fix の残作業**(/eco-fix eco-093): VC-TAG-11 から probe 生成(是正前赤=47 件で caption 交差)→
TagEditorViewModel の PreviewChips を先頭 3 件+ほか N 件表示派生へ(chip.moreItems 再利用)→
TagEditorWindow.axaml のプレビュー帯へ非対話「ほか N 件」テキスト→機械受入→R7→gate②。

## 8. 実施記録(2026-07-15 /eco-fix)

**プローブ先行(R5)**: CpUi093PreviewSummaryTests 2 本を CAD VC-TAG-11 から生成。是正前赤を実測 —
47 件テスト FAIL(プレビュー帯チップ Expected 3 / **Actual 47**=全件列挙・診断どおり)。
少数 2 件 pin は緑(G-6/ECO-087 承認済み視覚の基準)。

**是正(案 B・k=3)**: TagEditorViewModel — `PreviewChips` を先頭 3 件へ(PreviewMaxChips=3=CAD VC-TAG-11)+
`PreviewMoreCount/Show/Label`(chip.moreItems= ECO-091/092 と共通キー・Loc 経由)。候補値編集への
追随は既存 RaisePreviewChanged へ 3 通知を追加。TagEditorWindow.axaml — プレビューチップ列の直後に
**非対話 TextBlock**(Classes=previewMore・FaintText・fontSize 12=mock 転写)。
diff= VM 1 箇所+XAML 1 要素+通知 3 行。既存の選択強調(先頭チップ)・数値/シンプル型プレビューは無改変。

**横断規約(ECO-080)**: 新規文言なし(chip.moreItems 共通利用)・XAML 直書きなし・VM/DB 不変。

**機械受入**: build 0/0・**Tests 743/743**(probe 2 本緑転)・Oracle 109+2skip(R6 不変)・validate_bom 0/0。

**セルフゴールデン(R7・面全体並置=3 分類。CAD mock=タグ作成ダイアログ.dc.html は本裁定で
先頭 3 件+ほか N 件へ改版済み=VPUI afc8878)**:

| # | 差分/次元 | 分類 |
|---|---|---|
| 先頭 3 件+非対話「ほか N 件」(N=非表示数)・単一行維持・caption 非交差 | 転写(probe 実測=mock PREVIEW_MAX=3 と同値) | — |
| 「ほか N 件」意匠(グレー小テキスト・カーソル/ホバーなし) | 転写(mock span cursor:default と同義) | — |
| 少数件(3 件以下)のプレビュー・先頭チップの選択強調・数値/シンプル型プレビュー | 不変(pin 緑+既存 CpTagDlg087 全緑=743 に包含) | — |
| mock のプレビューチップはクリックで選択値切替(対話)/実装は非対話 Border | **既存差分**(ECO-087 以前からの実装状態・本 ECO の対象外=プレビューチップの対話性は VC-TAG-11 の範囲外) | 記録済み |

転写漏れ 0。

## 9. 残ゲート(更新)

- gate②(golden)のみ。
