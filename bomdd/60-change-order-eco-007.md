# Change Order — ECO-007(タグタブ UI/UX 表示契約の更新 — モック準拠)

> タグタブ UI/UX モック(`work/tag-tab/`)のレビュー決定を E-BOM へ反映する。発生源は **UI-IR/UI-BOM 抽出**(`bomdd/ui/`)— HTML+JS モックから抽出した UI-BOM 候補を read-across し、既存 E-BOM 表示契約との差分5件をユーザー(maintainer)が裁定した。
> **帰属: design_decision** — 欠陥ではなく、Ver2 タグタブの表示契約をモック準拠で**設計者が更新**する。うち3件(E1/E2/E3)は過去に意図的に入れた是正(E1/E2=ECO-004、E3=v2.0 GF-04)の**差し戻し**であり、撤回根拠を本書に明記する。
> 是正は v1.3 method どおり **仕様→E-BOM→M-BOM→Control Plan を同期 → fresh factory 再製造**(直接修正しない)。

## 0. 変更前 baseline
- As-Built: commit 3adc590(ECO-006 適用後)。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は **表示のみ**(状態遷移・スキーマ・永続データ変更なし)。固定オラクル追加行なし(表示は golden + VM 不変条件で担保)。
- データ fixture: N/A(表示のみ・永続変更なし)。

## 1. 変更要求
- ECO-ID: **ECO-007**
- 発生契機: タグタブ UI/UX モック(`work/tag-tab/` M1=タグ管理 / M2=タグ作成ダイアログ / M3=配置タグ設定 / S=仕様書)のレビュー → UI-IR/UI-BOM 抽出(`bomdd/ui/`)→ read-across で既存 E-BOM 表示契約との差分5件をユーザー裁定。
- 内容: タグタブの表示契約を **モック準拠** に更新(E1〜E5)。
- 種別: **設計決定(表示契約更新)**。E1/E2/E3 は過去是正の意図的差し戻しを含む。
- 原因が宿った上流成果物: 該当なし(欠陥ではない)。本 ECO は表示契約の**設計更新**。

## 2. 影響分析(トレース逆引き)
| ID | 画面/部品 | 決定 | E-BOM | UI-BOM(出自) | REQ/§ | 差し戻し |
|---|---|---|---|---|---|---|
| E1 | タグタブ ビュー行 | お気に入り★削除・名前+タグ数・説明 tooltip | E-UI-TAGS-026 | TMP-UI-CMP-0001 | REQ-030 §2.6 | **ECO-004 DC-VIEWLIST-001 DE-2/DE-3** |
| E2 | タグパレット カード | 説明欄は不要 | E-UI-TAGS-026 | TMP-UI-CMP-0003 | REQ-021 §2.6 | **ECO-004 DC-TAGPALETTE-001 DE-4** |
| E3 | 「タグを配置」ボタン | 汎用ラベル採用(対象タグ名/非活性を撤回) | E-UI-NODEGRAPH-025 | TMP-UI-ACT-0023 | REQ-034 §2.6 | **v2.0 GF-04** |
| E4 | グローバル検索(⌘K) | 無し(不採用) | E-UI-SHELL-021 | TMP-UI-ACT-0030 | §2.6 | なし(E-BOM 未記載) |
| E5 | タグ作成ダイアログ 付与プレビュー | 実付与 UI との表示一致を契約化(**現実装に無い新規部品**) | E-UI-TAGS-026 ↔ E-UI-TAGASSIGN-029 | TMP-UI-CMP-0012 | REQ-026/046 §2.6 | なし(新規契約) |
| E6 | タグ作成ダイアログ 種別セレクタ | ComboBox(現実装)→ セグメント3タブ(モック) | E-UI-TAGS-026 | TMP-UI-CMP-0009 | REQ-020 §2.6 | なし(表示) |
| E7 | タグ作成ダイアログ カラー | プリセット18色(Material-500)→ モックの9色(Radix系) | E-UI-TAGS-026 / E-DESIGN-028 | TMP-UI-CMP-0010 | REQ-021 §2.6 | なし(デザイントークン) |
- **影響なし予測(凍結)**: 横断固定オラクル(S-01〜S-31)・既存 unit の挙動は不変(表示のみ)。反証条件=本是正後に Oracle/既存 unit が赤化。表示は VM フィールド公開の新規/改訂 unit と golden で担保。
- **E4 注**: モックの⌘K UI 要素は不採用。E-UI-SHELL-021 に検索の記述は無く、E-BOM 改訂は不要(UI-BOM へも昇格しない)。

## 3. 表示契約マニフェスト改訂(製造パッケージの中核)
> 工場はこの表の target を実装する。required 要素のデータは既存(VM フィールド・整形器)。

### DC-VIEWLIST-001 改訂 — タグタブ ビュー一覧(E1)
| DE | 要素 | ECO-004(旧) | ECO-007(新) |
|---|---|---|---|
| DE-1 | ビュー名 | 有 | 維持 |
| DE-2 | お気に入り★ | `View.IsFavorite` を ★ 表示 | **削除**(タグタブ ビュー行に★を出さない) |
| DE-3 | 説明 | 2行目に truncate 表示 | **tooltip 表示**(行内 truncate をやめ hover tooltip。空なら非表示) |
| DE-4 | タグ数バッジ | 規定なし | **追加**(配置タグ数を行末バッジで表示) |
- 撤回根拠: Ver2 タグタブは「階層が主役・クロムは静かに脇へ」の設計方針。ビュー行は識別に必要な最小要素(名前+規模)に絞り、★/説明は行のノイズを増やすため非表示/tooltip 化する。`View.IsFavorite` 自体はデータとして残す(他画面での利用は別途)。

### DC-TAGPALETTE-001 改訂 — タグパレット行(E2)
| DE | 要素 | ECO-004(旧) | ECO-007(新) |
|---|---|---|---|
| DE-1 | 色チップ | 有 | 維持 |
| DE-2 | タグ名 | 有 | 維持 |
| DE-3 | タグ型 | 有 | 維持 |
| DE-4 | 説明 | 行に truncate/tooltip 表示 | **削除**(パレットカードに説明を出さない) |
- 撤回根拠: パレットカードは種別別サマリ(候補値チップ/数値範囲)で既に情報密度が高く、説明はカードを過密にする。`Tag.Description` はデータとして残し、作成/編集ダイアログでのみ編集・参照する。

### GF-04 撤回 — 「タグを配置」ノード追加ボタン(E3)
- 旧(E-UI-NODEGRAPH-025 不変条件・v2.0): 「ノード追加ボタンは対象タグ名を表示・パレット未選択時は非活性」。
- 新(モック準拠): **汎用ラベル「タグを配置」**(対象タグ名を出さない)。活性/非活性条件は配置動線(D&D 主・ボタン従)の設計に委ねる。
- 撤回根拠: モック(M3)は配置を D&D 主導線とし、ヘッダの「タグを配置」は汎用エントリ。対象タグ名のボタン反映は D&D 主導線と二重で、ラベルを汎用化する。**※ GF-04 は v2.0 の gap-fix。撤回は機能後退ではなく配置動線設計の変更**(D&D で対象を選ぶ)である旨を golden レビューで確認する。

### DC-TAGPREVIEW-001 新規 — タグ作成/編集ダイアログ 付与プレビュー(E5)
| DE | 要素 | 契約 |
|---|---|---|
| DE-1 | シンプル | 値なし表現が実付与 UI(E-UI-TAGASSIGN-029)の simple 表示と一致 |
| DE-2 | テキスト | 候補値チップ(選択=塗り)が実付与 UI のテキスト付与表示と一致 |
| DE-3 | 数値(★) | ★並び(単位=★・span 0..9 整数)が実付与 UI の★表示と一致 |
| DE-4 | 数値(プレーン) | 数値±ステッパ表示が実付与 UI(NumericTagDialog 系)と一致 |
- 契約: タグ作成ダイアログのプレビューは「画像に付けたときの見え方」を示す。**この付与表現は画像タブの実付与 UI(E-UI-TAGASSIGN-029)と表示一致させる**(★/数値±/候補チップ)。整形ロジックは核側ヘルパに置き unit 検査可能にする(pixel-exact 不採用)。

### タグ作成ダイアログ 表示差分(E5/E6・補足)
> 現実装 `TagEditorWindow` は**既に1ダイアログ適応型**(作成/編集兼用・`IsVisible={IsTextual/IsNumeric}` で型別パネル出し分け)。**構造の作り直しは不要**で、作成ダイアログ関連は表示差分+プレビュー追加に閉じる。

| 要素 | 現実装 | モック(target) |
|---|---|---|
| 種別セレクタ(E6) | ComboBox(ドロップダウン) | セグメント3タブ(シンプル/テキスト/数値) |
| 付与プレビュー(E5) | **無し** | 下部にライブプレビュー(新規部品・DC-TAGPREVIEW-001) |
| カラー(E7) | プリセット18色(Material-500)+hex | **モックの9色(Radix系)+hex ＝採用** |

- **E7(カラー確定)**: 種別プリセットを**モックの9色**(`#e5484d #f2912b #e8b931 #30a46c #12a594 #2f6bed #8b5cf6 #e93d82 #5b6473`)へ統一(現実装の Material-500 18色を置換)。E-DESIGN-028 / K-DESIGN のトークン更新。**データ低リスク**: 色は自由 hex(プリセットは入力ショートカット)で、既存タグの色(hex)は保持され影響なし。
- 構造(部品構成): **1ダイアログ + 共通部(基本情報/プレビュー)+ 型別設定パネル3つ**(TMP-UI-CMP-0015 simple / -0016 text / -0017 number)。型別パネルは差し替わるが**ダイアログ自体は単一**(現実装と同一構造)。今回の作成ダイアログ変更は「構造変更ではなく見え方+プレビュー追加」。

## 4. BOM 改訂(同期 — 製造時に適用)
> 本 ECO は**起票**段階。以下は製造/同期で適用する計画(`30-ebom.yaml` 等は本コミットでは未編集)。

- 仕様: `20-spec.md` §2.6 のタグタブ表示契約を E1〜E3 で改訂・E5 で DC-TAGPREVIEW-001 追記。
- E-BOM:
  - `E-UI-TAGS-026`: DC-VIEWLIST-001 / DC-TAGPALETTE-001 の invariant を ECO-007 版へ改訂。DC-TAGPREVIEW-001 を追加。「(ECO-004) …説明…」系 invariant に ECO-007 改訂注記。
  - `E-UI-NODEGRAPH-025`: GF-04 invariant(対象タグ名表示・未選択非活性)を撤回し ECO-007 注記。
  - `E-UI-TAGASSIGN-029`: DC-TAGPREVIEW-001 の read-across 相手として参照を追加。
- M-BOM: 該当 M-UI 単位の display_contract(required_elements)を E1/E2/E5 で更新。
- Control Plan: **CP-DISPLAY-PARITY-022** の検査行を更新(ビュー行=★無し/タグ数有り・パレット=説明無し・プレビュー=DC-TAGPREVIEW-001)。CP-UI-G6(golden)に E1〜E3/E5 の視覚項目。
- 固定オラクル: 追加なし(表示は論理オラクル対象外)。S-01〜S-31 不変。
- bom_rev: v4.0 → v4.0(eco:ECO-007)。

## 5. 部分再製造(隔離工場・spec-first)
- 製造パッケージ: 本 ECO-007(§3 マニフェスト)+ 改訂 BOM + 既存 src。
- 非開示: 原典 view-prism / `tests/ViewPrism2.Oracle` / `41-fixed-oracle.yaml`(工場隔離)。表示要素は §3 が供給。
- 改修想定: `TagsTabView.axaml`/`ViewRowViewModel`(E1)・`TagPaletteViewModel`/パレットテンプレ(E2)・`MainWindow.axaml` ノード追加ボタン(E3)・タグ作成ダイアログ View + プレビュー整形ヘルパ(E5)。状態遷移・評価ロジックは不変。

## 6. 受入(計画)
- unit: CP-DISPLAY-PARITY-022 改訂(ビュー行に★非公開・タグ数公開、パレットに説明非公開、プレビュー整形ヘルパの種別別出力)。
- 回帰: 既存 Tests + Oracle(S-01〜S-31)回帰ゼロ(表示のみ・挙動不変)。
- golden: タグ管理(ビュー行★無し+タグ数・パレット説明無し)・配置(「タグを配置」汎用ラベル)・作成ダイアログ(プレビュー=実付与 UI と表示一致)を CP-UI-G6 で視覚承認。E3 は「D&D 主導線で対象選択」を併せて確認。

## 7. 製造記録
- **製造済(factory-run-05・隔離工場 general-purpose)** — 2026-06-16。詳細: [reports/factory-run-05.md](reports/factory-run-05.md)。
- 変更: Core `TagAssignmentPreviewBuilder.cs`(新規・E5 純粋整形器)/`ViewService.GetHierarchyCountAsync`(E1)。App VM `TagsTabViewModel`(E1/E3)・`TagPaletteViewModel`(E2)・`TagEditorViewModel`(E5/E6/E7)。View `TagsTabView.axaml`(E1/E2)・`TagEditorWindow.axaml`(E5/E6/E7)。i18n ja/en 5キー。Tests `CpTagPreview001Tests`(新規11)・`CpDisplayParity022Tests`(A-4/A-5 を ECO-007 契約へ改訂)。
- 検証(メイン独立再実行): build Debug 0警告/0エラー。Tests 406/0・Oracle 74 pass/2 skip(S-01〜S-31 退行ゼロ)。`tests/ViewPrism2.Oracle`/`41-fixed-oracle.yaml` 未読・未変更(castability 規律遵守)。
- cheat 5件(全 minor): CHEAT-067 代表値=中点round / 068 テキストチップ先頭選択 / 069 ±ステッパ表示専用 / 070 K-DESIGN 寸法 / 071 E3 を root/child 2ボタンのまま維持(単一化見送り)。
- **残: golden 視覚承認(CP-UI-G6・E1〜E7)=承認者 maintainer 待ち**。実アプリ起動中(単一インスタンス)のためライブ目視は未実施。

## 8. provenance / lesson 連結
- 本 ECO は **UI-IR/UI-BOM 方法論(`BomDD/method/ui-ir-ui-bom.md`)の初適用**の産物。HTML+JS モックを UI-IR→UI-BOM 化し、既存 E-BOM へ read-across した結果、表示契約差分5件を機械的に捕捉してユーザー裁定に乗せられた(`bomdd/ui/ui-trace-map.json` で逆引き可能)。
- lesson: 「モックは E-BOM の前段=観測層」。フォワード設計のモック変更を、UI-BOM trace を介して既存 E-BOM 契約の差分(特に**過去是正の差し戻し**)として顕在化できる。差し戻しは黙って消さず、撤回根拠を ECO に残す(§3)。
