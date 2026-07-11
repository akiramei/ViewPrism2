# Change Order — ECO-069(staged): ECO-025 v2ソートが作業タブへ展開されず旧固定UIが残る

> maintainer所見(2026-07-12)「画像タブと作業タブのソートボタンが異なり、画像タブが正しいはず」を
> `/eco-file`で受理し、ECO-025のread-across範囲を工程診断した既存機能拡張/品質是正要求。

## 1. 症状・要求(maintainer報告・2026-07-12)

- 画像タブと作業タブで、中央ブラウズのソートボタンと操作モデルが異なる。
- 画像タブは「並び替え」ボタン+現在列バッジ、メニュー内の候補と昇順/降順、ソート中の要約チップ、
  リスト列ヘッダーを持つ。作業タブは現在列名ボタン+別体の方向矢印、固定3候補だけの旧UIである。
- maintainer見解: 画像タブ側が正しいはず。起票要求は「ECO-025の作業タブread-across漏れ+
  作業タブ固有CPの欠測」。
- 再現: 画像タブと作業タブをgrid/listで切り替え、ツールバーのソートボタン、popup、方向変更、
  解除、リストヘッダー、タイル補助値を比較する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **適用範囲が矛盾・gate①必要** | `work_tab.md` L14は中央ブラウズ/ソート/grid-listを画像タブと「同一部品・同一意味論」、構造差一覧にもソート差を挙げない。一方`decisions/FL-002-004-persistence.md` §1は「作業タブは旧3択固定モデル、FL-003適用範囲は画像タブ」「作業タブへのv2展開は別裁定・別ECO」と明記する。`file_list.md` v2は旧固定メニューをsupersededとするが、アクティブビューを持たないworkspaceでの候補供給/タグ列/表示列編集を未定義。 |
| 要求・仕様 | **画像タブv2は健全、作業タブへの適用は未確定** | REQ-081/仕様§2.6/E-UI-BROWSE-022は画像タブのアクティブビュー`display_columns`を軸にv2ソートを定義し、旧固定メニュー廃止を明記。REQ-074/E-UI-WORKSPACE-043は作業タブを「ソート可」とするが、REQ-081の全契約をworkspaceへどう射影するかは持たない。E-UI-WORKSPACE-043はE-UI-BROWSE-022へ依存するためread-across要求とも読め、CAD矛盾がBOMへ波及している。 |
| M-BOM・検査 | **作業タブ固有CP欠測** | M-UI-WORKSPACE-029はshellに「ソート」とだけ記載し、旧/v2、候補、解除、list header、grid tileを固定しない。CP-UI-G1は作業タブの多数のread-acrossを持つがECO-025ソートparityなし。CpUiG1WorkTabTestsは方向変更と表示順をECO-068で間接使用するだけで、UI語彙/状態/解除/list header/grid-list共有を検査しない。 |
| 実装 | **現CADの一方には適合、最新画像タブとは乖離** | WorkTabの旧UIは`f211fa9`(2026-06-29)導入。ImageTabはECO-025 `72e47c3`(2026-07-02)でv2へ刷新したがWorkTabを変更しなかった。2026-07-04裁定資料はその差を認識して別裁定へ送っているため、現状を無条件に実装バグとは断定できない。gate①でA採用後はWorkTabの実装/read-across欠陥として是正可能。 |

## 3. 切り分け済みの事実

### 3.1 確定

- ImageTab v2 (`ImageTabView.axaml` L608以降):
  - grid=`並び替え`+現在列badge(`なし`含む)+候補popup+popup内昇順/降順。
  - list=クリック可能な列header+sort要約chip+解除✕。
  - grid/listで`sortCol/sortDir`共有。名前以外のsort時はgrid tileにsort項目値を表示。
- WorkTab (`WorkTabView.axaml` L546以降):
  - `SortLabel`(名前/更新日/サイズ)をボタン文言にし、方向を別`sortDirBtn`で切替。
  - popupは固定3候補+checkのみ。sort解除、未sort、要約chip、badge、popup内方向segmentなし。
  - list headerはTextBlockでclick不可。grid tileにsort項目値なし。
- ImageTabはviewなしでも既定候補`name/size/modified_date`を生成する既存fallbackを持ち、
  `CpUiG1ImageTabSelectionTests`でexact固定済み。したがってworkspaceへv2を基本3列限定で射影する技術的先例はある。
- WorkTabのsort状態はFL-002=S-aにより画面ローカル/揮発で確定済み。表示形式の独立永続FL-004=D-bは
  本件で変更しない。
- 履歴: WorkTab旧モデル=`f211fa9`(2026-06-29)。ImageTab v2=`72e47c3`(2026-07-02)。
  FL裁定資料による明示的defer=2026-07-04。潜伏ではなく**認識済み未裁定差分がCP/backlogへ閉じず残留**した形。

### 3.2 疑い・未検証

- 案AでImageTabの`SortOptionVM`/`ViewColumnSorter`/cell描画を再利用できる範囲と、WorkTab固有VMへの
  最小adapter境界は未検証。`/eco-fix`前にcompile/runtime probeで確定する。
- WorkTabのlist headerをv2化した際、既存virtualization、各文脈mode、整理検索結果、viewer表示順に
  回帰がないかは未実測。

### 3.3 未確定事項との関係

- **FL-003**: ImageTabのv2統一ソートは確定済み。本ECOはWorkTabへの射影だけを裁定する。
- **FL-002**: sort状態は両タブとも画面ローカル/揮発のまま。永続化は対象外。
- **FL-004**: grid/list表示形式はタブ別独立永続のまま。対象外。
- **FL-001**: gridにtable列を持ち込まない決定を維持。案Aでもgridへの露出は並び替え候補と
  sort中タイル補助値だけ。

## 4. 是正方針候補(gate①)

### 案A — v2基本3列parity(推奨)

作業タブはアクティブビュー/表示列編集を持たないため、候補を既定基本3列
`name / size / modified_date`へ限定し、それ以外はImageTab v2と同じ操作契約にする。

- grid: `並び替え`+現在列badge、popup内3候補+昇順/降順、要約chip+解除、名前以外はtileにsort項目値。
- list: 固定3列headerをsort入口にし、active強調/方向、要約chip+解除。
- grid/listでsort状態共有、未sortは名前昇順、sort状態は画面ローカル。
- 表示列編集、タグ列sort、workspace schema追加は行わない。
- 規模: 中。WorkTab VM/XAML+共有sort model adapter+unit/headless+CAD/BOM/CP。DB/schemaなし。
- golden: ImageTabとの並置、grid/list往復、3候補/方向/解除/header/tile、タグ絞り込み/viewer順、各文脈mode。

### 案B — 視覚だけ画像タブ風へ寄せ、旧固定意味論を維持

ボタン語彙/badge/popup配置だけ変更し、list header・解除・未sort・tile補助値は追加しない。

- 規模: 小。
- 問題: `同一部品・同一意味論`を満たさず、同じ見た目で能力が違う新たな誤認を作る。非推奨。
- golden: toolbarのみ。read-across欠測は残る。

### 案C — workspace独自の表示列/タグ列sortまで導入

workspaceごとの表示列構成または候補母集合を新設し、ImageTab v2の全能力を持たせる。

- 規模: 大。CAD新surface、所有権、永続範囲、workspace schema/settings、列編集UI、移行/Oracleが必要。
- 問題: 今回のUI不一致を越える新機能で、利用者要求の証拠がない。非推奨。

## 5. 影響BOM(案A採用時)

- CAD: `ViewPrismUI/docs/screens/work_tab.md`にv2基本3列射影を明記、
  `decisions/FL-002-004-persistence.md`のdeferを裁定済みへ更新、live spec/review_points同期。
- 要求/仕様: REQ-074またはREQ-081 read-across注記、`20-spec.md` §2.6 WorkTab射影。
- E-BOM: E-UI-WORKSPACE-043へE-UI-BROWSE-022 v2 sortの基本3列限定継承を明記。
- M-BOM: M-UI-WORKSPACE-029 sort contract。
- 実装: WorkTabView.axaml、WorkTabViewModel、必要な共有末端sort VM/cell builder。ImageTab挙動は不変。
- 検査: CpUiG1WorkTabTests+headless XAML、CP-UI-G1へ潜伏/認識済みdefer残留履歴を追加。
- Oracle: 既存固定行変更なし(R6)。DB/Coreドメイン不変予測。必要なら新unit行のみ。

## 6. 残ゲート

1. ~~**gate① ViewPrismUI裁定**: 案A/B/Cから選択。推奨は案A。~~ → 案A採用・完了(§7)
2. CAD裁定コミットを製品へ取り込んだ後、`/eco-fix ECO-069`で先行赤probe→是正→機械受入。
3. gate② golden: 選択案の操作契約を画像タブと並置確認。
4. `/eco-accept ECO-069`でCP/As-Built/register/教訓をクローズ。

## 7. gate①裁定(2026-07-12)

- maintainer裁定: **案A=v2基本3列parityを採用**。
- 作業タブのsort候補は`name / size / modified_date`。アクティブビュー、表示列編集、タグ列sort、
  workspace schemaは追加しない。
- gridは`並び替え`+現在列badge（未sort=`なし`）+popup内候補/昇降順+要約chip/解除+tile補助値、
  listは固定3列header+active/方向+同じchip/解除。列/方向はgrid/list共有、未sort=名前昇順。
- FL-002=S-a、FL-004=D-b、FL-001の不変条件は維持。
- ViewPrismUI CAD反映: `3d76313` (`work_tab.md`、FL-002/004裁定追補、FL-003 review/live spec)。
- gate①完了。次の明示入口は`/eco-fix ECO-069`。この裁定コミットではsrc/testsを変更しない。
