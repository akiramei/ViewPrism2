# ECO-117: ScrollViewer.Padding 禁止則の lint 新設と残存 4 サイトの是正

- 起票日: 2026-07-20
- 報告者: ECO-116 R8 独立レビュー(51-cheat-log 2026-07-20 の分離起票候補の実行)
- baseline: main `928616d`
- 種別: 検査器新設(欠陥クラス lint)+実装是正(同クラス残存サイトの掃討)

## §1 症状・要求

**欠陥クラス**: `ScrollViewer.Padding` は Avalonia で Viewport から引かれず Extent とも
一致しないため、**内容末尾が `Padding.Top` のぶんスクロール到達不能**になる
(ECO-116 で実測確定: `Padding="16"` 一律→16px / `"16,14,16,20"`→14px)。

**潜伏の構造(ECO-117 R8 で時系列を訂正)**: この禁止則は `ViewerWindow.axaml:317-318`
(GF-TAGCTRL 実測・2026-07-01)と `ViewEditDialog.axaml:86-87`(ECO-025 GF-5・2026-07-02)に
remedy(内容 Margin 方式)つきで注意書きされたが、**その時点で既に製造済みだった違反 8 サイト
(2026-06-16〜29 混入)への遡及掃射が行われず**、全数が潜伏し続けた(4 サイト= ECO-116 で
是正済み・4 サイト=残存)。起票時の「注意書きの後に 8 サイトが書かれた」は git 遡りで反証された
時系列誤り。**教訓の正形= 法則発見時は遡及掃射(read-across)+機械検査(lint)まで込みで
初めて規約になる**(ECO-107 教訓 1「掃射手段の紐づけ」と ECO-003 readacross_lesson の合流)。

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

## §7 実施記録(2026-07-20 — 是正+機械受入完了・golden 待ち)

### 7.1 プローブ先行(R5)

- **到達性 probe(新規 `CpUiG1TrashPopupScrollTests`・CP-UI-G1)**: ゴミ箱 2 面(deleted 80 件で
  WrapPanel を溢れさせ、開いた状態で最大送り→内容末尾の可視を関係式検査= ECO-116 様式)。
  **是正前赤の実測**: 両面とも content.Bottom=585.0 > viewport.Bottom=567.0 =
  **到達不能量ちょうど 18px = Padding.Top**(法則の 3 例目・16/14/18 で三点確認)。
- **lint probe(新規 `CpAxamlLintScrollViewerPaddingTests`・CP-AXAML-LINT-117)**:
  要素属性ルール=**是正前 4 件で赤**(起票時の grep 悉皆と一致)。Style Setter ルール=
  起票時から 0 件(白の維持を pin)。

### 7.2 是正

- 4 サイトの `ScrollViewer.Padding` を内側 `ItemsControl` の `Margin` へ移動(値は 18 / 18 /
  12,0,12,8 / 14,0,14,8 のまま=余白量不変)。diff = XAML 3 ファイル・実質 8 行(+コメント 4 行)。
- **K-AVALONIA 刻印**(`31-kbom.yaml`): 禁止則+実測法則(16/14/18px)+機械検査の紐づけ+
  時系列訂正済みの教訓。

### 7.3 機械受入(4 点)

- `dotnet build --no-incremental`: **0 error / 0 warning**
- `dotnet test tests/ViewPrism2.Tests`: **837/837**(新規 probe 2 本+lint 2 本・是正前は 3 本赤)
- `dotnet test tests/ViewPrism2.Oracle`: **109 pass + 2 skip**(既知)
- `python bomdd/validate_bom.py`: **0 error / 0 warning**

### 7.4 R7(セルフゴールデン)

余白量不変の是正につき captures 並置ではなく実測 pin(ECO-116 §8.4 と同方式):

- ゴミ箱 2 面= probe 内で内寄せ 18/18 を恒久 pin。
- TagsTab 2 面= 使い捨て headless probe で実測(コミットせず撤去):
  Views リスト insetL=12 / insetT=0 / insetR=12・Palette リスト insetL=14 / insetT=0 / insetR=14
  = **旧 Padding と完全一致**。到達性も §3.2 の予測どおり(Top=0 =症状なし)を確認。
  水平スクロールバーの新規出現なし(Avalonia 12 の SV 既定= Horizontal Disabled を
  リフレクション実測・R8 レビュー)。転写漏れ 0。

### 7.5 R8(セルフレビュー)

fresh context の独立レビュー(退行実験・git 遡り・regex 意味論の実証込み)。所見と処置:

- **スコープ内 1 件(本 ECO 内で是正済み)**: **K-AVALONIA/lint コメント/本 ECO §1 の
  「後発 8 サイトが全違反」という因果主張が git 履歴と矛盾**。実測= 違反 8 サイトは全数が
  注意書き(2026-07-01/02)より**前**(2026-06-16〜29)に製造済みで、「注意書き後に書かれた」は
  時系列的に不成立。→ 6 文書を訂正(31-kbom / lint doc コメント / 本 ECO §1 /
  ECO-116 本文 §8.2・§10.3 に訂正注記 / CP-UI-G1 の「再発」表現 / register findings +
  cheat-log へ訂正記帳)。教訓の正形=「法則発見時は遡及掃射+lint まで込みで初めて規約になる」。
  結論(lint の必要性)自体は不変。
- **白を確認した項目(レビュー側の実証つき)**: XAML 等価性(SV 既定 Horizontal=Disabled の
  リフレクション実測・空状態・末尾余白)/lint の捕捉性(HEAD 抽出で是正前 4 件全捕捉・
  `\b` の偽陽性なし・`[^>]*?>` の越境なし)/probe の退行感度(Padding 復元で 18px 赤を再現→
  sha256 一致で復元)。
- **理論値の残余(現コードベースに 0 件を grep 確認・記録のみ)**: プロパティ要素構文
  `<ScrollViewer.Padding>`・属性値内生 `>`・入れ子 Style の早期終端・子孫コンビネータ
  セレクタの過剰検出(過剰側=保守的)。
- **未処置のスコープ内所見= 0**。

## §8 残ゲート

**gate②(golden・maintainer 実機)のみ。** 基準は §6 のとおり。
