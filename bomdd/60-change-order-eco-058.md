# Change Order — ECO-058(implemented / golden pending): 作業タブ中央ブラウズの非仮想化 — 1万件で6.5GiB超・応答不能

> maintainer の性能調査要求を受け、2026-07-10 に実コード読解と隔離データで実測した欠陥。
> 起票時点では工程診断と変更台帳登録のみで `src/tests` は変更しなかった。
> 2026-07-10 の `/eco-fix eco-058` でプローブ先行の最小是正と機械受入まで完了し、現在は maintainer golden 待ち。

## 1. 症状（maintainer 報告・2026-07-10）

作業タブ中央ブラウズが全画像のセルとサムネイルを一斉に実体化し、画像件数が多い作業スペースを開けない。

### 1.1 再現条件

- Release ビルドの実 Avalonia UI を、`VIEWPRISM2_DATA_DIR` で隔離したデータ領域に接続。
- 同期フォルダ直下に `normal` 画像 10,000 件、デフォルト作業スペース所属 10,000 件、
  `image_tags` 50,000 行（各画像 5 タグ）を投入。
- 元画像とウォームキャッシュは JPEG 256×192。キャッシュ 10,000 ファイルを全数ヘッダ検査し、
  寸法集合が `256x192` の 1 種だけであることを確認。
- 比較対照として同じ構造の画像 1 件データも測定。各安定区間の複数試料の中央値を採用。

### 1.2 観測値（MiB）

| surface | 件数 | Working Set | Private Memory | UI 状態 |
|---|---:|---:|---:|---|
| ImageTab | 1 | 226.2 | 136.0 | 応答あり |
| ImageTab | 10,000 | 331.2 | 239.6 | 応答あり・10,000 項目表示完了 |
| WorkTab | 1 | 266.4 | 172.6 | 応答あり |
| WorkTab | 10,000 | **6,749.4 以上** | **6,698.4 以上** | **切替未完了・「応答なし」** |

WorkTab はメモリ増加中にウィンドウが「応答なし」となり、ImageTab の最終フレームから切り替わらず、
アクセシビリティ木も取得不能になった。端末保護のためプロセスを停止したので、6,698.4 MiB は
完了時ピークでなく**観測下限**である。一時計測ハーネスとデータは測定後に削除し、製品コードの変更はない。

## 2. 工程診断（R2）

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD（ViewPrismUI） | **健全・裁定不要** | `docs/screens/work_tab.md:14` は中央ブラウズを画像タブと「同一部品・同一意味論」、`:52` は画像タブのレイアウト不変条件を全面継承、`:83` はグリッドカード/リスト行を同一部品と明記。大量件数だけ別部品に縮退する定義はない |
| 要求／E-BOM | **性能意図は既定** | REQ-041 は大量件数を UI 仮想化し、探索プローブで 1 万件操作可能を観測する。E-UI-BROWSE-022 は非仮想 `ItemsControl` 直置きを禁止し FMEA-013 を参照。E-UI-WORKSPACE-043 は同部品 E-UI-BROWSE-022 に明示依存（30-ebom:459） |
| M-BOM／Control Plan | **read-across 漏れ** | M-UI-WORKSPACE-029 と E-UI-WORKSPACE-043 の acceptance は CP-WORKSPACE-028/CP-L1-SMOKE/CP-UI-G1 のみで CP-NFR-026 を含まない。FMEA-013 の unit は旧 M-UI-013 のまま。P-01 は未観測かつ画像タブ固有操作（列数/ビュー切替）中心で、WorkTab 1 万件切替を明示しない |
| 実装 | **逸脱** | `WorkTabView.axaml:772-824` は `ScrollViewer > ItemsControl > WrapPanel`、`:837-860` のリストも素の `ItemsControl`。各テンプレートが `ThumbnailImage` を持ち、10,000 件を全実体化する。対照の ImageTab は `ItemsRepeater+UniformGridLayout`（`:942-1023`）と `VirtualizingStackPanel`（`:1095-1100`） |

### 2.1 診断分岐

- 欠陥の中心は、既定済みの中央ブラウズ性能契約に対する**実装逸脱**。
- 同時に、WorkTabへ契約を継承させる M-BOM／Control Plan／探索プローブの**検査被覆漏れ**が
  逸脱をマスキングした。`/eco-fix` では台帳被覆を先に是正してからプローブ・実装へ進む。
- CAD の意味論は確定済みで、仮想化方式は REQ-041 が実装自由としているため human gate①の裁定は不要。

## 3. 切り分け済みの事実

### 3.1 確定

1. **表示要素層が支配的**: 同一 10,000 件の ImageTab は Private 239.6 MiB で応答する一方、
   WorkTab は 6,698.4 MiB 以上で切替不能。DB 件数だけでは差を説明できない。
2. **グリッド/リスト双方が非仮想**: WorkTab の `Items` 消費面はいずれも画面外項目を抑制する
   パネルを持たず、サムネイルを含む DataTemplate を全件生成する構造。
3. **混入コミット**: `f211fa9a`（2026-06-29、ECO-020/021）で WorkTab VM/XAML が導入され、
   非仮想構造も同時に入った。
4. **read-across 漏れの確定点**: ECO-026 は 2026-07-02 に ImageTab の同じ
   `ItemsControl+WrapPanel` 欠陥を FMEA-013/REQ-041 不適合として診断し、
   `51582a43` で ImageTab だけを仮想化した。WorkTab は E-UI-BROWSE-022 依存であったが対象・受入に
   含まれず、同型構造が残った。
5. **未確定事項との非関係**: UQ-W06=B（作業タブ側でオーケストレートする隔離方式）は
   VM/モードの所有境界であり、同一中央ブラウズの仮想化契約を解除しない。
   FL-004（表示形式のタブ独立永続）も保存キーの裁定で、実体化戦略とは無関係。

### 3.2 起票時の未検証事項（fix後の扱い）

- 是正前10,000件WorkTabの完了時ピークは、安全停止したため引き続き不明（再実行不要）。
- 是正後のgrid/list別メモリとscroll後値は §4.4 / P-01 で測定済み。
- 仮想化方式は ImageTab と同じ ItemsRepeater/UniformGridLayout/VSP を採用。残るVM/DB/Bitmap負荷の
  詳細分解は、本ECOの操作可能性回復に不要なためR3で混ぜず、将来必要時に別ECOで測る。
- 固定メモリ上限や秒数の製品目標は未固定のまま。現行契約は「1万件で操作可能」の探索観測である。

## 4. 実施した是正（`/eco-fix eco-058`）

### 4.1 BOM／検査面を先に閉じる

- E-UI-WORKSPACE-043/M-UI-WORKSPACE-029 に、中央ブラウズが E-UI-BROWSE-022 の
  仮想化不変条件（画面外セル/サムネ非実体化・非仮想 ItemsControl 直置き禁止）を継承すると明記した。
- 既存 CP-NFR-026 を WorkTab へread-acrossし、CP-UI-G1 に 10,000 件のgrid/list受入観点を追加。
  FMEA-013 は実所有者 M-UI-IMAGETAB-035 へ是正し、WorkTab同型事故を FMEA-038 として分離した。
- P-01 を ImageTab/WorkTab の両 surface とgrid/list/scrollへ拡張し、是正前/後を同じ構造の
  256×192ウォームキャッシュ10,000件で記録した。

### 4.2 プローブ先行（R5）

- `CpUiG1WorkTabTests` に256項目・1366×900固定viewportのheadless実レイアウトプローブを追加した。
  visual tree 上の実効可視セルを grid/list 別に数え、各surfaceが全件の半数未満であることを要求する。
- 製品修正前に追加プローブだけを走らせ、既存572件合格に対し追加1件が
  `Items=256, grid=256, list=256` で不合格になることを実測した（R5の赤）。
- 最小修正後は同プローブを含む573件が全合格。壁時計や静的grepだけでなく、実体化数で再混入を拒否する。

### 4.3 最小実装

- 画像タブで実績のある grid `ItemsRepeater+UniformGridLayout` と list
  `ItemsControl+VirtualizingStackPanel` を WorkTab のレイアウトホストへ同型適用した。
- DataTemplate、`Items`、PointerPressed、選択/タグドット/整理マーカー、ViewModel、DB は変更していない。
  ページング、軽量プレースホルダー、DB/VM/Bitmapキャッシュ最適化は混ぜなかった（R3）。

### 4.4 探索受入結果

- 同構造の実UI 10,000件で WorkTab への切替、grid/list往復、双方のscroll、grid復帰が完了し、
  全プロセス試料で応答あり。タグドット、件数、画像、リスト列も描画された。
- WorkTab grid は Working Set median 434.5 MiB / Private median 333.9 MiB、list は
  490.9 / 388.9 MiB、list連続scroll後は 500.4 / 399.1 MiB。是正前 grid の Private
  6,698.4 MiB以上（観測下限）に対し少なくとも20.1分の1となり、数GiB増加・切替不能は再現しなかった。
- 固定MiB/秒閾値は新設せず、探索値は P-01 に記録。視覚・選択・文脈モードの最終確認はgoldenへ残す。

## 5. 影響 BOM

- `E-UI-WORKSPACE-043`（E-UI-BROWSE-022 仮想化契約の継承明記、acceptance read-across）。
- `M-UI-WORKSPACE-029`（WorkTabView/VM 製造・acceptance read-across）。
- `CP-NFR-026`、`CP-UI-G1`（WorkTab再検査範囲）。
- `FMEA-013`（ImageTab実所有者へ修正）と `FMEA-038`（WorkTab read-across漏れを新設）。
- `P-01`（両surface・grid/list・10,000件のbefore/after）。
- 実装対象: `WorkTabView.axaml` のレイアウトホストのみ。`WorkTabViewModel`/DB/Core は対象外。
- CAD変更なし。既存固定 Oracle 行は変更しない（R6）。

## 6. 残ゲート

1. golden（maintainer実機）: WorkTab 10,000 件で切替完了・スクロール可能、grid/list往復と
   選択/文脈モードの視覚退行なし。
2. `/eco-accept eco-058`: CP 観点・register applied・本文クローズの3点セット。

## 7. 機械受入（2026-07-10）

- Release build: **0 warning / 0 error**。
- `ViewPrism2.Tests`: **573/573 pass**（既存572 + ECO-058実体化数プローブ1）。
- `ViewPrism2.Oracle`: **109 pass / 既知2 skip**。既存固定Oracle行・Oracleコードは無変更。
- `validate_bom.py`: **0 error / 0 warning**。変更YAML 5件は個別parse成功。
- 一時再計測ハーネス/画像/DB/cacheは P-01 転記後に全削除し、成果物へ含めていない。

**停止点**: `/eco-fix` のgolden基準に到達。CAD裁定・追加性能最適化は不要。
maintainer実機golden合格後に `/eco-accept eco-058` でクローズする。
