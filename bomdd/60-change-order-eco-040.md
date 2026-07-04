# Change Order — ECO-040(staged): タグ編集「タグ追加」検索ボックスの入力カレットが上寄り(垂直中央でない)

> maintainer 報告(2026-07-05・スクリーンショット付き)の視覚欠陥是正。起票時に工程診断
> (mock/UI-IR/BOM/実装 — ECO-025 retro 規律)を実施済み。

## 1. 症状(maintainer 報告・2026-07-05)

- 画像タブ・タグ編集モード・右ペイン「タグ追加」タブの**タグ検索ボックス**で、
  **入力カレット位置が上に寄っている**。自然な位置(ピル内の垂直中央)にするべき。
- スクリーンショット実測: フォーカス時の入力枠・カレットが 40px ピルの中央より上に描画される。

## 2. 工程診断 — 欠陥は実装層に局在(CAD/BOM 改訂不要)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全** | mock `資料/画像タブ/ViewPrism2 画像タブ.dc.html` L385–387: 検索ボックスは `display:flex; align-items:center` の 40px ピル内 input(border:none)= **垂直中央が設計原器で確定**。`docs/screens/image_tab.md` L304「タグ追加欄には検索があります」 |
| BOM | **健全** | E-UI-TAGASSIGN-029 所属(ui-trace-map TMP-UI-INP-0020/TMP-UI-ACT-0060・handling: bom) |
| 実装 | **欠陥** | ImageTabView.axaml の TextBox 垂直整列指定漏れ(§3) |

- 未確定事項(FL-*/VE-*)との関係: 該当なし(視覚整列は CAD 確定済み・純粋な実装欠陥)。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定(コード読解+履歴):

- 該当 XAML: `src/ViewPrism2.App/Views/ImageTabView.axaml:178-179` —
  `<TextBox ... VerticalAlignment="Center" Padding="0" Width="240" />`。
  **`VerticalContentAlignment` 未指定**のまま `Padding="0"` でテーマ既定の内側余白を潰している。
- 混入コミット(git log -S で確定): `e10767b`(2026-06-17・画像タブ製造 M1+M2)導入時から
  = **潜伏 約 18 日**。
- マスキング要因(疑いでなく構造): 検索ボックスは **VM 未配線(Text="" 固定)**で入力する
  機会が実運用に無く、フォーカス時のカレット位置が観測されにくかった(未配線自体は
  スコープ外所見 → §7)。
- 対照(read-across・同型構造の全数列挙): 「ピル Border+BorderThickness=0/Padding=0/
  VerticalAlignment=Center・VerticalContentAlignment 無し」は他に 4 箇所 —
  ImageTabView.axaml:422/426(整理トレイ 条件入力 2 本)・WorkTabView.axaml:387/390(同・作業タブ)。
  同一欠陥様式の可能性が高い(是正時に全数プローブ)。
- 非対象(構造が異なる): タグタブのパレット検索(TagsTabView.axaml:245-246)は Padding 上書き
  なしの素の TextBox(Height=40)= テーマ既定余白が生きており本症状の報告なし。
  作業タブの「タグ追加」パネル(WorkTabView.axaml:180-182)には検索ボックス自体が無い。

疑い(未検証 — /eco-fix のプローブで実測):

- 機構仮説: Avalonia Fluent の TextBox は MinHeight(≈32)とテーマ既定 Padding を持ち、
  `Padding="0"` 上書き+`VerticalContentAlignment` 未指定により、TextPresenter(テキスト+
  カレット)が TextBox 領域の上端に寄る — 結果 40px ピルの中央より上に描画される。

## 4. 是正方針(案 — 着手時に確定)

- 最小是正: 該当 TextBox に `VerticalContentAlignment="Center"` を付与(XAML のみ・1 属性)。
- read-across: §3 の同型 4 箇所(整理トレイ条件入力×画像/作業タブ)を実機/headless で実測し、
  同症状なら**同一欠陥様式として本 ECO 内で一括是正**(ついで修正ではなく同一欠陥の全数処置)。
- プローブ: 是正前に headless レンダリングで TextPresenter の垂直位置を実測し
  「上寄り」を数値で裏取りしてから触る(R5)。
- 再発防止(クローズ時に確定): テーマ既定 Padding を `Padding="0"` で潰す時は
  `VerticalContentAlignment` を明示する — ピル内埋込 TextBox の製造規約として CP へ明記。

## 5. 影響 BOM

- impacted: E-UI-TAGASSIGN-029(surface)/ M-UI-013(ImageTabView.axaml 資材所有・
  品目トレースは M-UI-IMAGETAB-035 経由)。read-across 拡張時は E-UI-SIMILARITY-035(整理トレイ)/
  E-UI-WORKSPACE-043・M-UI-WORKSPACE-029(WorkTabView.axaml)へ波及。
- CAD・E-BOM 改訂なし(§2 診断どおり実装層に局在)。オラクル影響なし(視覚のみ・R6)。

## 6. 残ゲート

1. 是正実施(/eco-fix eco-040 — プローブ先行)
2. 機械受入: build 0 / Tests / Oracle / validate_bom 0 error
3. golden(maintainer 実機): タグ追加 検索ボックスのカレット・テキストがピル垂直中央
   (+read-across 是正時は整理トレイ条件入力も同観点)
4. クローズ時: CP 観点明記(§4 再発防止)+register status 更新

## 7. スコープ外所見(R3 — 51-cheat-log へ記録済み)

- **タグ追加 検索ボックスが未配線**: `Text=""` 固定・VM(ImageTabViewModel)に AddQuery 相当の
  プロパティ/絞り込みが存在しない。CAD は `addQuery / onAddSearch` による候補絞り込みを定義
  (mock L387・trace-map TMP-UI-INP-0020/ACT-0060 は handling: bom)= **CAD 定義済み機能の実装欠落**。
  本 ECO(視覚整列)に混ぜず、起票要否は maintainer 判断(51-cheat-log 2026-07-05 所見参照)。
