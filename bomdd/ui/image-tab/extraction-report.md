# 抽出レポート — 画像タブ UI-IR / UI-BOM

> 生成: 2026-06-17 / 方法: BomDD `method/ui-ir-ui-bom.md`(テーマ/思想を上位部品=designIntent として認識する拡張版)
> 入力: `ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ.dc.html`(M)+ `docs/screens/image_tab.md`(S)+ `docs/01_design_direction.md`(D)+ `docs/02_mock_fidelity_policy.md`(P)
> 出力先: `ViewPrism2/bomdd/ui/image-tab/`(タグタブは `../` に平置き)

## 1. 抽出概要

画像タブは共通シェル下の**可変3ペイン**(左=コレクション常時/中央=ブラウズ常時/右=タグ編集モード時のみ)。中核思想は**二軸ブラウズ**(ファイルシステム軸 ⇄ タグビュー軸)を**同じ「潜る・戻る」操作**で扱うこと。タグタブで組んだ「ビュー」を画像タブが閲覧軸として消費する。

- 画面 1(SCR-0002)・領域 4(REG-0001 共有 + 0005/0006/0007)・部品 16(CMP-0020〜0035)・出現 5(OCC-0010〜0014)・操作 24(ACT-0040〜0063 + 共有 0029)・入力 1(INP-0020)・状態 13(STA-0020〜0033)・業務概念 5 新規(DOM-0020〜0024)+ 共有 6。
- **共有部品/概念**は tag-tab の仮品番を再利用(種別チップ CMP-0004・カラードット CMP-0005・タグ/種別/ビュー/候補値/数値範囲/カラー DOM)。新規 id を振らないことで「同じ候補=同じ仮品番」を維持。
- designIntent: DESIGN DIRECTION バンドを [design-intent.md](design-intent.md) へ抽出(原則3。nonBom にしない)。

## 2. BOM 対象/対象外の判断

### BOM 化したもの
- 画面・3ペイン領域・ツールバー部品(軸/編集/ソート/レイアウト)・パンくず・タグチップ・グリッド/リスト・タグ編集パネル(タブ/現在タグ/タグ追加/インライン値)・各操作・状態(選択/空状態/モード)。
- 業務意味・操作・状態・入力・受入観点を持つ要素のみ。

### BOM 対象外(nonBom / trace)
- titlebar / menu strip / ⌘K 検索 / 設定 → シェル所有(E-UI-SHELL-021)。trace か nonBom。
- direction band → **designIntent**(設計憲章。捨てない)。
- 整理 / 作業 / ⋯ ボタン → 表示はするが挙動未定(UQ-I07)。trace。
- ※ダミー画像注記・装飾 wrapper → nonBom。
- 画像クリック時のビューア起動 → スコープ外(E-UI-VIEWER-024)。

## 3. E-BOM 連携候補(既存 E-BOM が画像タブ領域を厚く保持)

タグタブと違い、画像タブは**既存 E-BOM に対応 surface が既にある**。本抽出は新規昇格より**既存品目への接続と乖離検出**が主眼。

| UI-BOM | 接続先 E-BOM(候補) | 連携の質 |
|---|---|---|
| 画面 SCR-0002 | E-UI-BROWSE-022 / E-UI-AXIS-NAV-040 / E-UI-MODE-041 / E-UI-TAGASSIGN-029 の上位束ね | 上位構成 |
| コレクションペイン REG-0005 | **E-UI-SHELL-021**(コレクション選択スコープ) | display 精緻化 |
| ブラウズ領域 REG-0006・グリッド/リスト CMP-0027/0028 | **E-UI-BROWSE-022**(グリッド/リスト・選択・空状態)・E-SORT-004・E-THUMB-020 | 直接対応(ECO-016 再帰属) |
| 表示軸ナビ CMP-0021/0025/0026 | **E-UI-AXIS-NAV-040**(FS/タグビュー軸・パンくず・チップ)・E-VIEWSVC-009・E-EVAL-002 | 直接対応(ECO-016 再帰属) |
| 文脈モード CMP-0022/ACT-0050/0061/0062/0063 | **E-UI-MODE-041**(タグ編集/整理/メンテ・クリック意味論)・E-UI-TAGASSIGN-029・E-UI-SIMILARITY-035・E-UI-MERGE-036 | 直接対応(ECO-016 再帰属) |
| タグ編集パネル REG-0007・付与 CMP-0033/0034/0035 | **E-UI-TAGASSIGN-029**(タグ付与パネル)・E-TAGSVC-008・REQ-046/026/027 | 直接対応(改訂候補) |
| タグチップ(view ナビ)OCC-0014・軸セレクタ CMP-0021 | **E-UI-AXIS-NAV-040**(ビュー階層の消費)・E-VIEWSVC-009 | 対応(E-UI-NODEGRAPH-025 はタグタブ中央エディタへ縮小) |
| タグ種別/候補値消費(追加行・候補値ピッカー) | **E-UI-TAGS-026**・E-DOMAIN-001(read-across) | 消費 |
| 種別チップ/カラードット/トークン | **E-DESIGN-028** / K-DESIGN | デザインシステム(共有) |

## 4. モック乖離(差分帰属)— 本抽出の核心

既存 E-BOM の想定とモックが**意図的に異なる**箇所。BomDD §9.3 の差分帰属で分類。**根幹3点(#1〜3)は 2026-06-17 maintainer 承認で確定(全てモック採用)**。#4〜6 は継続(タグタブ UQ-E1〜E5 と同じ手続きで ECO 化する)。

| # | 乖離 | 帰属 | 決定 / 是正 | UQ |
|---|---|---|---|---|
| 1 | タグビュー軸ナビが **左 NodeGraph ツリー → 中央の軸セレクタ+チップ+パンくず** | `design_decision` | **✓決定: モック採用**。E-UI-NODEGRAPH-025 を「タグタブ中央エディタ」に絞り、画像タブ左ツリー+ECO-006/BL-001 を撤回 | UQ-I01 |
| 2 | タグ値入力が **numeric 固定値/連番ダイアログ → 右パネル内インライン** | `design_decision` | **✓決定: インライン採用**。固定値=インライン。連番は別アクションで存置(UQ-I02b) | UQ-I02 / I02b |
| 3 | グリッドセルに **ファイルサイズを出さない**(リスト列のみ) | `display_contract_gap` | **✓決定: サイズ除去**(モック準拠)。リスト列のサイズは維持。ECO-004 の差し戻し根拠を ECO に明記 | UQ-I05 |
| 4 | **コレクション内サブフォルダの FS 階層ブラウズ** | `design_decision` | **✓決定(ECO-011): FS フォルダ軸採用**。relative_path 派生・新スキーマ/スキャン不要・読取専用(INV-009)。表示軸=FS フォルダ/タグビューの2軸+全画像フラット入口維持。原典フォルダ+パンくず復権 | UQ-I04 |
| 5 | コレクション行に **相対パス表示・276/64 折り畳み** | `display_contract_gap`(軽) | 未決。E-UI-SHELL-021 コレクション一覧 display contract の精緻化 | UQ-I03 |
| 6 | 新規 surface 部品 6点(軸セレクタ・ソート・パンくず・グリッドカード・リスト行・パネルタブ) | `design_system_part_missing`(予防) | [design-system-bom.md](design-system-bom.md) の `new` を製造前に Components.axaml へ | — |

> **重要**: #1・#2 は「設計の根幹」。E-UI-NODEGRAPH-025 と E-UI-TAGASSIGN-029 は v1.2 で原典(view-prism)から起こした surface であり、本モックはそれを**作り直す**判断。**ECO-010 起票 + E-BOM(30)同期済**(2026-06-17・`bomdd/60-change-order-eco-010.md`)。spec/M-BOM/Control Plan の全面同期と実装は画像タブ製造フェーズ。

## 5. read-across ギャップ

- **タグ付与表現の表示パリティ(DC-TAGPREVIEW-001)**: タグタブの作成ダイアログ「付与プレビュー(★/数値±/候補チップ)」と、画像タブ実付与 UI(本タブの現在タグピル/インライン値ピッカー)の表示一致が契約済(ECO-007)。本モックのインライン数値は `★ N` 表記・候補値チップで整合的だが、**プレビューの ± 操作 vs 実付与のセル選択** の表現差を golden で突合する必要(CP-DISPLAY-PARITY-022)。
- **選択視覚(GF-02)**: グリッド=セル枠+チェック、リスト=行淡青、で E-UI-BROWSE-022 の v2.0/GF-02 由来契約に整合。退行させない。
- **タグ定義の単一正本**: 画像タブのタグ追加候補・候補値・数値範囲は E-UI-TAGS-026/E-DOMAIN-001 が正本。画像タブで再定義しない(消費のみ)。
- **base タグ vs 付与タグ**: モックは base タグ(画像が元から持つ)を付与/削除で emulate。実装での base/付与の区別とカスケードは UQ-I09。

## 6. 次段への申し送り

1. **unresolved 全件決着(2026-06-17)**: 根幹4決定(I01 ナビ・I02 付与インライン・I05 グリッド・I04 表示軸)=ECO-010/011、残 UQ(I03/I06〜I15・DS1/DS2)=ECO-012 でクローズ。**画像タブ設計フェーズ完了**。詳細は [unresolved-questions.md](unresolved-questions.md)。
2. **E-BOM 同期(ECO-010〜016)**: 確定分につき E-UI-NODEGRAPH-025 / E-UI-TAGASSIGN-029 / 旧 E-UI-GRID-022(+ DC-GRID-001)を `30-ebom.yaml` で同期し、ECO-016 で E-UI-BROWSE-022 / E-UI-AXIS-NAV-040 / E-UI-MODE-041 へ追跡付き再分割済。
3. **Design System BOM ゲート**: [design-system-bom.md](design-system-bom.md) の `new` 6 部品を製造計画へ。Components.axaml 再利用が前提。
4. **製造**: golden-in-the-loop(実機 ⇄ モック M 突合)。E1〜E7・GF 是正・固定オラクルを退行させない。
