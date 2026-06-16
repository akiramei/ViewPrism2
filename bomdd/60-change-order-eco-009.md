# Change Order — ECO-009(タグタブ デザイン言語移植 — 原典素朴 → モック CAD)

> モック(CAD)の **DESIGN DIRECTION 01/02/03** に明記された設計意図(`bomdd/ui/design-intent.md`)を実機へ適用する。実機は原典 view-prism の素朴デザインに忠実で、golden 基準も原典だったため「統一思想・部品言語」が欠落していた(`bomdd/ui/visual-gap-tag-tab.md` の製造検査で全量を棚卸し)。
> **帰属: design_decision(設計意図の実装)**。欠陥ではなく、CAD のデザイン言語を未実装のまま放置していた設計負債の解消。golden 基準を**モックへ切替**し、golden-in-the-loop で製造。
> 重要な是正: 初回 UI-IR 抽出で DESIGN DIRECTION を「マーケ・nonBom」と誤分類し破棄していた(maintainer 指摘で再抽出 → `design-intent.md` 正典化)。これが「実機に思想が無い」根本原因。

## 0. 変更前 baseline
- As-Built: commit c8a4f9a(ECO-007 Phase B 製造後)。
- 状態: 機能を満たす最小スケルトン(素の Border/DockPanel・グリフ文字ボタン・条件=素テキスト・Fluent 既定アクセント #3B82F6・Inter)。デザインシステムのコンポーネント層が未構築。
- **表示のみ**(状態遷移・スキーマ・永続データ・固定オラクル 不変)。

## 1. 変更要求
- ECO-ID: **ECO-009**
- 発生契機: タグタブ実機 vs モック(CAD)の視覚突合で、設計言語(カード/チップ/CTA/アイコン/コンテナ/状態色)の大規模乖離が判明(`visual-gap-tag-tab.md`)。maintainer 指摘で設計意図を再抽出(`design-intent.md`)。
- 内容: タグタブ3ペイン+ダイアログを CAD のデザイン言語へ移植。`①テーマ基盤 → ②左ペイン → ③中央ペイン`(右パレットは先行実証)。
- 種別: 設計決定(デザイン言語移植)。

## 2. 影響分析
| 対象 | 改訂 | E-BOM / K-BOM |
|---|---|---|
| デザイントークン | アクセント #2F6BED 統一・状態色(選択淡青/ホバー淡灰)・角丸スケール・カード/チップ/CTA トークン | E-DESIGN-028 / K-DESIGN(ECO-009 項) |
| 再利用コンポーネント層 | Card / 型チップ / 候補値チップ / 範囲ピル / 条件チップ / CTA / アイコンボタン / バッジ / コンテナカード | `src/ViewPrism2.App/Styles/Components.axaml`(新規) |
| 左 ビュー管理 | CTA・ビューカード行・先頭アイコン・選択/ホバー状態 | E-UI-TAGS-026 |
| 中央 階層構造 | コンテナカード・型チップ・条件チップ(型別配色)・アイコンボタン・淡青選択 | E-UI-NODEGRAPH-025 |
| 右 タグパレット(先行) | カード・色型チップ・候補値/範囲・CTA・件数/凡例 | E-UI-TAGS-026 |
- **影響なし(凍結)**: 固定オラクル S-01〜S-31・既存 unit の挙動。表示のみ。

## 3. デザイン言語(正典)
- 正典: **`bomdd/ui/design-intent.md`**(DESIGN DIRECTION 01/02/03 由来)。思想「ひとつの部品言語で全機能を統一」・4原則・トークン・部品言語。
- 実装: **`Styles/Components.axaml`**(再利用部品)+ **`App.axaml`**(トークン)。**個別ビューに色/角丸を直書きせず**トークン・コンポーネント class を再利用する。

## 4. BOM 改訂(同期 — 適用済)
- K-DESIGN(`31-kbom.yaml`): 「V1 最小セット+原典踏襲」→ モック CAD 由来の統一デザイン言語へ昇格(ECO-009 項追加・design-intent.md 参照・青統一・IBM Plex 方針・FluentTheme グローバル上書き)。
- UI-IR(`bomdd/ui/ui-ir.json`): DESIGN DIRECTION を nonBom → designIntent へ訂正。
- E-DESIGN-028: アクセント #2F6BED・9色・状態色・部品トークン(design-intent.md)。
- 仕様/固定オラクル: 変更なし(表示のみ)。bom_rev: v4.0(eco:ECO-009)。

## 5. 製造(段階実行・golden-in-the-loop)
メイン(隔離工場でなく直接実装・高フィデリティ優先)で段階製造し、各段で build/test/golden:
- ① テーマ基盤(commit d9946c8): アクセント #2F6BED(SystemAccentColor 含む)・全コントロール角丸・フォーカス青。
- ② 左ペイン(13003b8): CTA・ビューカード行・アイコンボタン・節見出し。
- ③ 中央ペイン(d84004e): コンテナカード・型チップ・条件チップ(amber/mono/緑)・アイコンボタン・配置 CTA・ヘルプ箱。
- 右パレット 先行実証(7d9b524): カード・候補値/範囲・色型チップ・CTA・件数/凡例。
- golden 調整(7d056d3): 行アイコン視認性(FaintText→TextMuted)・TreeView 選択を淡青(PART_LayoutRoot)。

## 6. 受入
- build: Debug 0 警告/0 エラー。
- test: ViewPrism2.Tests 406/0・ViewPrism2.Oracle 74 pass/2 skip(固定オラクル S-01〜S-31 退行ゼロ・各段で確認)。
- golden(承認者 maintainer): **①②③+パレット 視覚承認済**(3ペイン+ダイアログが同一部品言語=原則03 充足)。行アイコン視認性・淡青選択も確認済。

## 7. 製造記録
- 製造: メイン直接実装(段階・golden-in-the-loop)。commit: 7d9b524 / d9946c8 / 13003b8 / d84004e / 7d056d3。
- cheat/申し送り: §8。

## 8. provenance / 残・lesson
- provenance: UI-IR/UI-BOM round(`bomdd/ui/`)→ 視覚ギャップ分析 → 設計意図再抽出(design-intent.md)→ 本 ECO。
- **残タスク**:
  - フォント **Inter → IBM Plex**: procurement の別タスクに分離(spawn task。OFL 確認・埋め込み・fallback)。UI 改善と外部資産導入のコミットを混ぜない方針。
  - 画像タブ/作業タブへの部品言語展開(Components.axaml は再利用可)。
  - シェル/ナビ(セグメント pill)・検索ボックス内アイコン・色ドットのリング等の細部 golden 調整。
- lesson: **CAD(モック+UI-IR)の DESIGN DIRECTION は設計意図そのもの**であり nonBom にしてはならない([[mock-ui-ir-is-cad]])。「V1 最小+既定任せ」では統一意図は出ず、設計意図を K-DESIGN に記録し**テーマ全体へ適用**して初めて手触りが揃う。視覚は unit でなく golden で確かめる(golden-in-the-loop)。
