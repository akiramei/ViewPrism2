# Change Order — ECO-004(表示パリティ read-across 是正・A 群)

> ECO-003 で追加した表示パリティ・ゲートの **CAPA read-across**(bomdd/reports/display-parity-readacross-2026-06-15.md)で確定した表示 omission(A 群 5 件)を是正する。
> 帰属: **spec_omission**(各画面の表示契約が要件化されていなかった)。是正は ECO-003 の規律どおり **仕様/E-BOM/M-BOM/Control Plan を同期 → 製造**。ECO-003 と異なり**製造前に BOM を先に整え、隔離工場で製造**(direct-fix しない)。
> 本書の §3 表示契約マニフェスト(DC/DE)が製造パッケージの中核 — 工場は原典 view-prism を見ず、本マニフェストの提示要素のみから製造する。

## 0. 変更前 baseline
- As-Built: Loop V4 / ECO-003 適用後(候補カード5要素化済み)。固定オラクル `tag:loop-v4-r1` は不変(表示は横断契約=論理では捕捉不能)。
- データ fixture: N/A(表示のみ・永続変更なし)

## 1. 変更要求
- ECO-ID: **ECO-004**
- 発生契機: ユーザー確認(maintainer)→ ECO-003 横展開の read-across
- 内容: 既存移植画面のうち、原典が提示する表示要素を欠く 5 箇所(A-1〜A-5)を是正
- 種別: **欠陥修正(表示 omission・spec_omission)**
- 原因が宿った上流成果物: 各画面の REQ/§(表示契約の脱漏)

## 2. 影響分析(トレース逆引き)
| A-ID | 画面 | 欠落 | E-BOM | M-BOM | REQ/§ |
|---|---|---|---|---|---|
| A-1 | RelinkWindow 候補 | サムネイル+ファイル名 | E-UI-REPAIR-039 | M-UI-REPAIR-027 | REQ-017/072 §2.11.5 |
| A-2 | 画像グリッド セル | ファイルサイズ | E-UI-GRID-022 | M-UI-019 系 | REQ-041/042 §2.6 |
| A-3 | トラッシュ 項目 | ファイルサイズ | E-UI-REPAIR-039 | M-UI-REPAIR-027 | REQ-071 §2.11 |
| A-4 | ビュー一覧 | お気に入り★+説明 | E-UI-TAGS-026 | M-UI 系 | REQ-030 §2.6 |
| A-5 | タグパレット | タグ説明 | E-UI-TAGS-026 | M-UI 系 | REQ-021 §2.6 |
- **影響なし予測(凍結)**: 横断固定オラクル(S-01〜S-30)・既存 unit の挙動は不変(表示のみ)。反証条件=本是正後に Oracle/既存 unit が赤化。表示は VM フィールド公開の新規 unit と golden で担保。

## 3. 表示契約マニフェスト(DC/DE)— 製造パッケージの中核
> 工場はこの表の required 要素を実装する。各要素のデータは既存(列・整形器)。

### DC-RELINK-001 — RelinkWindow 候補カード(A-1, A-6)
| DE | 要素 | source | 現状 | target |
|---|---|---|---|---|
| DE-1 | サムネイル | original | 無 | `ThumbnailImage SourcePath={AbsolutePath}`(RepairWindow と同型) |
| DE-2 | ファイル名 | original | 無 | `RelinkCandidateViewModel.FileName`(既存プロパティ)を主見出しに |
| DE-3 | 相対パス | original | 有 | 維持 |
| DE-4 | サイズ | original | 有 | 維持 |
| DE-5 | 更新日時 | original | 有 | 維持 |
| DE-6 | missing 行ファイル名 | original | 無(パスのみ) | `MissingImageViewModel.FileName` を主表示に(A-6) |
- 実装要点: `RelinkViewModel` に `ISyncFolderRepository` を注入し collection root を解決→`MissingImageViewModel`/`RelinkCandidateViewModel` に AbsolutePath 供給(RepairViewModel と同パターン)。`WindowService.ShowRelinkAsync` で `_folders` を渡す。`RelinkWindow.axaml` 候補テンプレを RepairWindow と同型のカードへ。

### DC-GRID-001 — 画像グリッド セル(A-2)
| DE | 要素 | source | 現状 | target |
|---|---|---|---|---|
| DE-1 | サムネイル | — | 有 | 維持 |
| DE-2 | ファイル名 | — | 有 | 維持 |
| DE-3 | ファイルサイズ | original | 無 | セル下(ファイル名の下)に `ByteSizeFormatter.Format(Record.FileSize)` を従テキストで追加 |
- 注: リスト表示の列には既にサイズ有り。**グリッドセルのみ**欠落。`ImageItemViewModel`/バインド元に SizeText を公開(既存 ByteSizeFormatter)。

### DC-TRASH-001 — トラッシュ項目(A-3)
| DE | 要素 | source | 現状 | target |
|---|---|---|---|---|
| DE-1 | サムネイル | — | 有 | 維持 |
| DE-2 | ファイル名 | — | 有 | 維持 |
| DE-3 | ファイルサイズ | original | 無 | `TrashItemViewModel` に `SizeText`(ByteSizeFormatter)公開→TrashView 項目テンプレに従テキスト追加 |

### DC-VIEWLIST-001 — タグタブ ビュー一覧(A-4)
| DE | 要素 | source | 現状 | target |
|---|---|---|---|---|
| DE-1 | ビュー名 | — | 有 | 維持 |
| DE-2 | お気に入り★ | original | 無 | `View.IsFavorite`(既存)を ★ アイコン/バッジで表示。`ViewRowViewModel` に IsFavorite 公開 |
| DE-3 | 説明(description) | original | 無 | `View.Description`(既存)を2行目に truncate 表示。空なら非表示 |

### DC-TAGPALETTE-001 — タグパレット行(A-5)
| DE | 要素 | source | 現状 | target |
|---|---|---|---|---|
| DE-1 | 色チップ | — | 有 | 維持 |
| DE-2 | タグ名 | — | 有 | 維持 |
| DE-3 | タグ型 | — | 有 | 維持 |
| DE-4 | 説明(description) | original | 無 | `Tag.Description`(既存)を行に truncate/tooltip 表示。空なら非表示 |

## 4. BOM 改訂(同期)
- 仕様: 20-spec.md §2.6/§2.11 に各画面の表示契約注記(本マニフェスト参照)
- E-BOM: E-UI-GRID-022 / E-UI-TAGS-026 / E-UI-REPAIR-039 に表示 invariant 追加(DC 参照)
- M-BOM: 該当 M-UI 単位に display_contract(required_elements)追加・FMEA-032
- Control Plan: **CP-DISPLAY-PARITY-022**(unit: 各 VM が表示フィールドを公開)+ CP-UI-G1/G6/G10(golden 視覚突合に DC 行追加)
- bom_rev: v4.0 → v4.0(eco:ECO-004)。固定オラクル不変

## 5. 部分再製造(隔離工場・spec-first)
- **製造パッケージ**: 本 ECO-004(§3 マニフェスト)+ 改訂 BOM + 既存 src。
- **非開示**: 原典 view-prism ソース / tests/ViewPrism2.Oracle / 41-fixed-oracle.yaml(工場隔離)。表示要素は §3 マニフェストが供給するため原典不要=castability の検証。
- 再利用: 既存 VM・ThumbnailImage・ByteSizeFormatter。

## 6. 受入
- unit: CP-DISPLAY-PARITY-022(各 VM の表示フィールド公開を exact 検査)→ **新規 CpDisplayParity022Tests 6 Facts 緑**
- 回帰: **Tests 387/0(既存 381+新規 6)・Oracle 73 PASS+2 skip(S-01〜S-30 回帰ゼロ)**・build 0 警告(Debug。Release は起動中アプリのロックのみ)
- **castability 検証=合格**: 隔離工場(原典 view-prism・固定オラクル非開示)が §3 マニフェストのみから A 群を製造。cheat **0 件**(BOM/マニフェストから全導出可)= 表示契約が要件として完全だった証拠
- golden(残): 各画面の DC 行を原典とフィールド突合(CP-UI-G1/G6/G10 再ウォークスルー)で視覚承認

## 7. 製造記録
- 工場: factory-06(fresh 隔離・general-purpose)。変更: RelinkViewModel/RelinkWindow.axaml/WindowService(A-1/A-6)・ImageBrowserViewModel/MainWindow.axaml(A-2)・TrashViewModel/TrashView.axaml(A-3)・TagsTabViewModel/TagsTabView.axaml(A-4)・TagPaletteViewModel(A-5)・CpDisplayParity022Tests(新規)。
- first-pass green・収束再製造なし・cheat 0。

## 8. lesson 連結
本 ECO は ECO-003 の read-across(CAPA 第4段)の産物。「ゲートを足したら過去の出荷物も一度測る」を実行し、同型 omission(A-1 RelinkWindow=ECO-003 の波及漏れ)を含む 5 件を捕捉・是正した。
