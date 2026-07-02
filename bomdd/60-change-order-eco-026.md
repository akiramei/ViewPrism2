# ECO-026: 画像タブ一覧の性能是正(グリッド仮想化 REQ-041 適合 + 一覧 VM 再構築/チップ件数/タグ列/サムネの最適化)

- **status**: staged(起票 + REQ/E-BOM/Control Plan 同期。実装は段階=性能オラクル→低リスク→グリッド仮想化・M4)
- **type**: 不具合是正(REQ-041/KBOM 仮想化不適合=FMEA-013 同型)+ 性能最適化(NFR)。CAD 視覚は不変(挙動・実体化戦略の是正)
- **baseline**: ECO-025 クローズ後(main `b939eff`)
- **bom_rev**: v4.0(eco:ECO-026)
- **入力**: maintainer 性能レビュー(調査のみ・2026-07-02)。6 所見 + REQ-041 不適合。ViewPrism2 実コードで全件裏取り済。
- **乖離時の権威**: 挙動・実体化は KBOM(31・K-AVALONIA)/REQ が正。視覚レイアウトは ViewPrismUI(CAD)。

## 1. 背景 — なぜこの ECO か

maintainer の性能レビューで、画像タブ一覧に 6 件の性能課題が指摘された。実コードで全件確認した結果、
**#1(アイコングリッド非仮想化)は REQ-041 と KBOM の明確な不適合**(ビューア scroll で是正した FMEA-013 の罠と同型が
グリッドに残存)であり、残り 5 件は VM 再構築・件数計算・タグ列引き・サムネ実体化の最適化。βのリスト仮想化時に
KBOM の「グリッドは仮想化パネルへ」指針が**グリッドへ横展開されていなかった**工程漏れ(S3 同族=方法論はあるが本 surface へ未適用)。

## 2. 所見(全件 confirmed・実コード)

| # | 所見 | 出所 | 分類 |
|---|---|---|---|
| 1 | アイコングリッドが `ScrollViewer>ItemsControl>WrapPanel`=**非仮想化**。全セル+サムネが一斉実体化 | `ImageTabView.axaml:809-811` | **REQ-041/KBOM 不適合(FMEA-013 同型)** |
| 2 | グリッド/リスト切替でも `Items` 全件再構築(`SetGrid`/`SetList`→`Recompute`→`Items.Clear`+全件 `ImageItemVM`+`BuildCells`) | `ImageTabViewModel.cs:1221,724` | 最適化(不要な全件再割当) |
| 3 | ビュー軸チップ件数=子ノードごと `ViewMatched` 再評価(`子数+1` 回の全件条件評価) | `ImageTabViewModel.cs:663-667` | 最適化(1 パス集計へ) |
| 4 | タグ列セルは 行×列×画像タグ数 の線形探索(`entry.Tags` 走査) | `ListColumnModel.cs:239` | 最適化(画像ごと辞書化) |
| 5 | 表示列ライブ編集ごとに一覧全件 `Recompute` | `ImageTabViewModel.cs`(`OnColumnPickerChanged`) | 最適化(再計算範囲縮小) |
| 6 | `ThumbnailImage` に detach/リサイクル時の明示 Release/キャンセルが無い(`ViewerImage` にはある) | `ThumbnailImage.cs` | 最適化(#1 仮想化でリサイクル時に効く) |

**ガード不在**: 一覧 VM 再構築・タグ列・サムネ実体化を測る決定論ガードが無い(`CpNfr001Tests` は条件評価器単体1000件のみ)。

## 3. net-new / 変更

### 3.1 グリッド仮想化(#1・REQ-041 適合・E-UI-BROWSE-022)

アイコングリッドを **KBOM 既定の仮想化方式**へ是正する: `ItemsControl+WrapPanel`(非仮想化)を廃し、
**ItemsRepeater(UniformGridLayout)または VirtualizingStackPanel の行リスト**に載せて画面外セルを実体化しない。
選択・タグドット・整理/選択バッジ・ソート項目タイルの視覚は不変(セル DataTemplate を再利用)。クリックは既存の
`PointerPressed`(コード側選択)を維持。**視覚 golden は不変を目標**(スクロール挙動=可視分のみ実体化のみが変わる)。

### 3.2 一覧 VM 再構築の範囲縮小(#2/#5)

- **表示形式切替(#2)**: `SetGrid`/`SetList` は `_layout` と表示切替のみで、`Items`(同一データ)を作り直さない。
- **表示列ライブ編集(#5)**: 列変更は列定義+各行セル(`BuildCells`)の再構築で足り、フォルダ/チップ/母集合の全 `Recompute` を回さない
  (除去列がソート中の解除は維持)。デバウンス可。

### 3.3 チップ件数の 1 パス集計(#3・E-UI-AXIS-NAV-040/BROWSE)

子ノード件数は、現ノードの母集合を 1 回評価し、各子ノード条件でバケット集計する(`子数+1` 回の全件評価を 1 パスへ)。
ソート規則・件数の値は不変。

### 3.4 タグ列引きの辞書化(#4・ListColumnBuilder)

`BuildCells` のタグ値取得を、画像ごとの `tagId→値` 辞書(または `(imageId,tagId)` インデックス)で O(1) 化。
セル表示内容は不変。

### 3.5 サムネの明示 Release(#6・ThumbnailImage)

`ThumbnailImage` に `OnDetachedFromVisualTree`(または SourcePath=null 経路)で **進行中ロードの CancellationToken キャンセル + Bitmap Dispose**(描画から外した後)を追加(`ViewerImage`/KBOM v2.0 Run2 と同じ規律)。#1 の仮想化リサイクルで効く。

## 4. 影響 BOM(REQ/E-BOM/Control Plan は本 ECO で同期。spec/M-BOM/KBOM prose は M4)

| 対象 | 是正 |
|---|---|
| REQ-041 | 現状追認+不適合是正: グリッド仮想化は**必須要件**(既存)。実装が ItemsControl+WrapPanel で未達だったのを KBOM 既定方式へ是正 |
| E-UI-BROWSE-022 | invariant: グリッドは仮想化パネル(ItemsRepeater/VSP 行リスト)必須=非仮想 ItemsControl 直置き禁止(FMEA-013 同型)。表示形式切替は Items を再構築しない。タグ列引きは O(1)。fmea_refs += FMEA-013 |
| E-THUMB-020 / ThumbnailImage | detach/リサイクル時に進行中ロードをキャンセルし Bitmap を Dispose(K-AVALONIA v2.0 Run2 規律をサムネへ横展開) |
| 33-control-plan | CP-NFR-026(新設): 一覧再構築/チップ件数評価回数/タグ列引き/サムネ Release の決定論ガード(壁時計でなく振る舞い) |
| 31-kbom | (M4)グリッド仮想化罠は画像タブグリッドにも適用と明記(ビューア scroll と同型・FMEA-013)。K-AVALONIA prose 同期 |
| 60-change-register | ECO-026 エントリ |

**触れない**: ソートモデル(FL-003 v2・ECO-025 で確定)・列描画契約(kind 別セル)・表示列モデル(ViewColumnModel)。視覚レイアウト(ViewPrismUI CAD)。

## 5. 段階分割

- **性能オラクル先行**: 退行を捕まえる決定論ガード(CP-NFR-026)。
- **低リスク(#3/#4/#5/#6)**: golden 面に触れない VM/部品の最適化。
- **グリッド仮想化(#1/#2)**: golden 面=maintainer 実機再確認(視覚不変が目標)。

## 6. 受け入れ基準

- REQ-041 適合: グリッドが仮想化パネルで、画面外セル/サムネを実体化しない(CP-NFR-026 ガード + 探索プローブ P-07 系)。
- 意味論不変: 選択/範囲選択/ソート/チップ件数/タグ列セル内容/型別描画は既存 Tests/Oracle 退行ゼロ。
- 視覚不変(golden): グリッド/リストの見た目は ECO-025 承認と同一(maintainer 実機)。
- validate_bom 0/0。

## 7. 検証(本 ECO=staged の範囲)

- `python bomdd/validate_bom.py`: 0/0。
- 起票時点: REQ/E-BOM/Control Plan 同期。実装は段階で unit(CP-NFR-026)+ 既存 Tests/Oracle 退行ゼロ + golden(maintainer 実機)。

## 8. 採番メモ

ECO-025 クローズ後の逐次採番で **ECO-026**。
