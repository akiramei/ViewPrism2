# ECO-117: ScrollViewer.Padding 禁止則の lint 新設と残存 4 サイトの是正

- 起票日: 2026-07-20
- 報告者: ECO-116 R8 独立レビュー(51-cheat-log 2026-07-20 の分離起票候補の実行)
- baseline: main `928616d`
- 種別: 検査器新設(欠陥クラス lint)+実装是正(同クラス残存サイトの掃討)

## §1 症状・要求

**欠陥クラス**: `ScrollViewer.Padding` は Avalonia で Viewport から引かれず Extent とも
一致しないため、**内容末尾が `Padding.Top` のぶんスクロール到達不能**になる
(ECO-116 で実測確定: `Padding="16"` 一律→16px / `"16,14,16,20"`→14px)。

**再発の構造(ECO-116 教訓 1)**: この禁止則は `ViewerWindow.axaml:317-318`(GF-TAGCTRL 実測)と
`ViewEditDialog.axaml:86-87`(ECO-013/GF-TAGCTRL-01 教訓)に remedy(内容 Margin 方式)つきで
**注意書き済みだったにもかかわらず**、その後に書かれた 8 サイトが全部違反した
(4 サイト= ECO-116 で是正済み・4 サイト=残存)。**注意書きは掃射手段を紐づけない限り
規約として機能しない**(ECO-107 教訓 1 の再実証 N=2)。

**要求 2 点**:

1. **lint 新設**: src の全 `.axaml` を走査し、`<ScrollViewer` 要素の `Padding` 属性を検出して
   不合格にする恒久検査(ECO-107 の資産 lint 様式= テストクラスとして実装・allowlist なしの
   ゼロ基線)。
2. **残存 4 サイトの是正**: lint をゼロ基線で緑にするため、残存サイトを ECO-116 と同じ
   remedy(内容 Margin へ移す・余白量不変=視覚不変)で掃討する。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全(非該当)** | 到達性は `overflow:auto` の定義に含まれる(ECO-116 §2 と同一の判定)。余白量は変えないため視覚契約に触れない |
| BOM | **検査の谷間(lint 不在)** | 欠陥クラスが 2 度実測されながら(GF-TAGCTRL-01・ECO-116)機械検査が存在せず、注意書きコメントのみ。K-AVALONIA 実装規約層に刻まれてもいない |
| 実装 | **逸脱 4 サイト残存(確定・§3.1)** | grep 悉皆済み。うち 2 サイトは `Padding.Top=18` で実害見込み |

結論: **検査器新設+実装層の掃討**。裁定(gate①)は不要です。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測)

**残存 4 サイト(grep 悉皆・src 全 `.axaml`)**:

| # | サイト | Padding | 予測到達不能量(=Top) | 混入 |
| --- | --- | --- | --- | --- |
| 1 | [ImageTabView.axaml:1463](../src/ViewPrism2.App/Views/ImageTabView.axaml)(ゴミ箱ポップアップ) | `18` | **18px(実害見込み)** | `2c589d8`(ECO-019) |
| 2 | [WorkTabView.axaml:1123](../src/ViewPrism2.App/Views/WorkTabView.axaml)(ゴミ箱ポップアップ) | `18` | **18px(実害見込み)** | ECO-019 系の read-across 面 |
| 3 | [TagsTabView.axaml:636](../src/ViewPrism2.App/Views/TagsTabView.axaml) | `12,0,12,8` | **0px(Top=0・非該当見込み)** | `b810ae7`(ECO-009) |
| 4 | [TagsTabView.axaml:861](../src/ViewPrism2.App/Views/TagsTabView.axaml) | `14,0,14,8` | **0px(Top=0・非該当見込み)** | 同上 |

- **Style 層は白**: `Styles/*.axaml` に ScrollViewer セレクタへの Padding Setter は存在しない
  (grep 済み)= lint の走査対象は要素属性のみで足りるが、Style 層も含めて走査すれば将来も閉じる。
- **法則の適用**: 到達不能量= `Padding.Top`(ECO-116 §8.1 の実測法則)。よって #3/#4 は
  Top=0 のため**症状は出ない見込み**だが、ゼロ基線 lint の成立(allowlist なし)と
  規約の一様性のため同時に掃討する。Bottom=8 は Extent 側の縮みとして
  下余白が 8px 詰まって見えている可能性はある(是正で正しい余白に戻る方向・要 R7 確認)。

### 3.2 未検証(fix 時に実測)

- #1/#2(ゴミ箱)の**実測**到達不能量(probe で赤を採る= R5)。
- #3/#4 の「症状なし」の実測確認(probe が Top=0 で緑のままであること=法則の追検証)と、
  Margin 化による視覚差分の有無(R7・Bottom=8 の効き方)。

## §4 是正方針(着手時確定)

1. **プローブ先行(R5)**: `CpUiG1TagPanelScrollTests` の到達性関係式(内容末尾判定)を
   ゴミ箱ポップアップ 2 面へ適用(是正前赤の実測)。TagsTab 2 面は法則どおり緑のままか確認。
2. **lint 新設**: ECO-107 資産 lint 様式のテストクラス(例: `CpAxamlLintScrollPaddingTests`)。
   src 全 `.axaml` を走査し `<ScrollViewer ... Padding=` を 0 件 pin(Style 層セレクタも走査)。
   **是正前は 4 件で赤**= lint 自体のプローブ。
3. **是正**: 4 サイトの Padding を内容側 Margin へ(ECO-116 remedy・余白量不変)。
4. **K-AVALONIA 刻印**: 実装規約層(`31-kbom.yaml`)へ「ScrollViewer.Padding 禁止
   (余白は内容 Margin)+機械検査= 本 lint」を追記(ECO-111 の廃止 API 刻印と同型)。
5. **R7**: ゴミ箱ポップアップは余白量不変で視覚不変見込み。TagsTab 2 面は Bottom 8px の
   出方に差分が出る可能性があるため内寄せ実測を probe に含める。

## §5 影響 BOM(見込み)

- **src**: `ImageTabView.axaml` / `WorkTabView.axaml` / `TagsTabView.axaml`(計 4 サイト・数行)。
- **tests**: lint 新設 1 クラス+ゴミ箱 2 面の到達性 probe(いずれも是正前赤)。
- **kbom**: K-AVALONIA へ禁止則+lint 紐づけを刻印。
- **CP**: lint は automated CP(ECO-107 様式)。golden 観点は CP-UI-G1 の ECO-116 刻印が既にカバー。

## §6 残ゲート

- **gate①(裁定)**: **不要**。
- **gate②(golden)**: **必要(軽量)**。maintainer 実機で
  「ゴミ箱ポップアップ(画像タブ/作業タブ)を件数多めで開き、最下端まで送って最終行が完全に
  見える+余白が従来どおり」+「タグタブの当該 2 リストの見た目が破綻していない」。
  lint 自体は機械証拠(0 件 pin の緑転)で足りる(ECO-105/111 前例)。
