# Change Order — ECO-058(staged): 作業タブ中央ブラウズの非仮想化 — 1万件で6.5GiB超・応答不能

> maintainer の性能調査要求を受け、2026-07-10 に実コード読解と隔離データで実測した欠陥。
> 本起票は工程診断と変更台帳登録のみで、`src/tests` は変更しない。

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

### 3.2 未検証（推測と分離）

- 10,000 件の WorkTab が構築を完了した場合の最終ピーク値（安全停止したため不明）。
- グリッドとリストを個別起動したときのピーク差（静的には双方非仮想だが、実測は既定 grid）。
- どの Avalonia 仮想化方式を採用するか、および仮想化後に残る VM/DB/Bitmap 負荷の割合。
- 固定メモリ上限や秒数の製品目標。現行契約は「1 万件で操作可能」の探索観測で、数値閾値は未固定。

## 4. 是正方針（案 — `/eco-fix` 着手時にプローブで確定）

### 4.1 BOM／検査面を先に閉じる

- E-UI-WORKSPACE-043/M-UI-WORKSPACE-029 に、中央ブラウズは E-UI-BROWSE-022 の
  仮想化不変条件（画面外セル/サムネ非実体化・素の ItemsControl 直置き禁止）を継承すると明記。
- CP-NFR-026 を WorkTab へread-acrossするか、WorkTab専用の決定論ガードを新設するかを
  プローブ設計時に確定。FMEA-013 の unit/target と CP-UI-G1 の再検査範囲を同期。
- P-01 を画像タブ/作業タブの両 surface に拡張するか、WorkTab 1 万件プローブを分離新設し、
  本 ECO の是正前/後観測値を記録する。

### 4.2 プローブ先行（R5）

- 256×192ウォームキャッシュの件数ランプで、是正前に
  「項目数に比例して実体化数・Private Memoryが増える」ことを再現可能な治具へ固定する。
- 端末保護の停止条件を持たせ、10,000 件での応答不能を無制限に再実行しない。
- 可能なら headless Avalonia で全件数に対する実体化要素数を観測し、壁時計だけに依存しない
  不合格プローブを先に追加する。成立しなければ実UI探索プローブ+構造ガードで担保する。

### 4.3 実装はプローブ後に選ぶ

- 第一候補は、画像タブで実績のあるグリッド `ItemsRepeater+UniformGridLayout` と
  リスト `VirtualizingStackPanel` の同型適用。セル視覚・選択/タグドット/整理マーカー等は維持する。
- ページングや軽量プレースホルダーは、仮想化が WorkTab の操作契約を満たさないと
  プローブで判明した場合の代案。現時点で採択しない。
- 仮想化後の再計測で残存支配項が確認されるまで、DB/VM/Bitmap キャッシュ最適化を本 ECO に混ぜない。

### 4.4 受入の方向

- 10,000 件で WorkTab への切替が完了し、UI操作とスクロールが可能。
- 画面外セル/サムネイルを全件実体化せず、項目数に比例する数 GiB 級の増加を再発させない。
- grid/list 双方で、選択、Shift/Ctrl、タグドット/絞り込み、各文脈モード、ソート、表示形式永続を退行させない。
- 視覚は現 CAD と既存 golden を維持。固定数値閾値は探索結果を記録し、要求を新設する必要が出た場合だけ別途裁定する。

## 5. 影響 BOM

- `E-UI-WORKSPACE-043`（E-UI-BROWSE-022 仮想化契約の継承明記、acceptance read-across）。
- `M-UI-WORKSPACE-029`（WorkTabView/VM 製造・acceptance read-across）。
- `CP-NFR-026` または本件用新規 CP、`CP-UI-G1`（再検査範囲）。
- `FMEA-013`（WorkTabへの unit/target read-across）。
- `P-01` または WorkTab 専用探索プローブ（42-exploratory-probes）。
- 将来の実装対象: `WorkTabView.axaml`。`WorkTabViewModel`/DB/Core は再計測で必要性が確定するまで対象外。
- CAD変更なし。既存固定 Oracle 行は変更しない（R6）。

## 6. 残ゲート

1. `/eco-fix eco-058`: BOM/検査面のread-across → 不合格プローブ先行 → 最小是正 → 再計測。
2. 機械受入: build 0 / Tests / Oracle（既知 skip のみ）/ validate_bom 0 error・0 warning。
3. golden（maintainer実機）: WorkTab 10,000 件で切替完了・スクロール可能、grid/list往復と
   選択/文脈モードの視覚退行なし。
4. `/eco-accept eco-058`: CP 観点・register applied・本文クローズの3点セット。

**診断停止点**: CAD裁定は不要。`/eco-fix eco-058` で是正に着手できる。
