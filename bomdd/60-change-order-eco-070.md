# Change Order — ECO-070(implemented / golden pending): FS表示でフォルダがソート対象外となり固定名前昇順で残る

> maintainer要求(2026-07-12)「ファイルシステム表示ではフォルダを先、その後にファイルを表示し、
> ソートはフォルダ・ファイルそれぞれに対して行う」を`/eco-file`で受理した既存機能品質是正要求。

## 1. 症状・要求(maintainer報告・2026-07-12)

- 画像タブのファイルシステム表示で、フォルダが現在のソート対象になっていない。
- 期待する一次グループ順は常に`フォルダ群 → ファイル群`であり、両群を混在させない。
- 二次順序は、フォルダ群とファイル群をそれぞれソートする。
- 再現: 直下に複数フォルダと複数画像があるコレクションをFS軸で開き、名前の昇順/降順を切り替える。
  ファイル群は反転するが、フォルダ群は常に名前昇順のまま残る。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **フォルダ行のsort意味論が未定義・gate①必要** | `docs/screens/file_list.md` §ソートは表示列を軸に「行」を並べる規則を持つが、FSフォルダ群を常に先頭へ置くtype-primary順、フォルダ群の比較キー、size/date/tag列選択時のフォルダ値を定義しない。権威v2 standaloneにもフォルダ行モデルがない。`image_tab.md`はFS階層遷移を定義するがsortとの合成規則を持たない。 |
| 要求・仕様 | **REQ-081の適用対象に穴** | REQ-081/仕様§2.6/E-UI-BROWSE-022は画像`ImageEntry`の基本列・タグ列比較を詳細化する一方、フォルダ行と画像行の一次グループ順、フォルダ群の二次比較を規定しない。IMG-015/ECO-060のスキャン中は取込順append・完了時sortという既存契約との合成も未記載。 |
| M-BOM・検査 | **フォルダ+ファイル混在fixtureが欠測** | M-UI-013のsort契約とCP-UI-G1/CpViewColumnSorterTestsはファイル列だけをpinする。`CpUiG1ImageTabSelectionTests.FileNames`も`!IsFolder`へ絞るため、フォルダ順が固定名前昇順でも全件緑となる。 |
| 実装 | **現行はグループ順を満たすがフォルダsortを明示除外** | `ImageTabViewModel`はFS解決時にfilesだけ`SortFiles`へ通し、foldersは`OrderBy(Name, OrdinalIgnoreCase)`へ固定する。`BuildItemsFromMatched`はfoldersを先に全件追加してからfilesを追加するため一次グループ順は既に正しい。Seed旧実装はfolders/filesを分割し同じ比較器で個別sort後concatしていたが、実データ版`45a6c77`(2026-06-18)でfolders固定昇順となった。ECO-025 v2 `72e47c3`はfile列sortだけを刷新し、この差を検出しなかった。 |

## 3. 切り分け済みの事実

### 3.1 確定

- 対象surfaceは画像タブの**FS軸だけ**。タグビュー軸と作業タブにはフォルダ行がない。
- 現在も表示順は`_matchedFolders`全件→`_matchedFiles`全件で、フォルダ先頭という要求の半分は満たす。
- フォルダの表示モデルは`Name/Count`だけで、size/date/tag値を持たない。list cellも名前以外は存在せず、
  ファイルの`ViewColumnSorter`へそのまま渡せない。
- 名前降順を選んでもfilesは降順、foldersは`OrdinalIgnoreCase`昇順のまま。未sort時もfoldersは名前昇順。
- スキャン中はIMG-015により公開済み項目を取込順appendし、sort条件は完了時適用する契約がある。
  本件でスキャン中だけフォルダを先行sortすると既存契約に反する。
- viewer列・選択範囲・類似検索候補は画像だけを対象とし、フォルダ行を渡さない既存意味論は不変。

### 3.2 疑い・未検証

- 通常ロード、パンくず移動、tag chip絞り込み、progressive scan完了の各再構築経路で同じgroup comparatorを
  消費できるかは`/eco-fix`の先行probeで確定する。
- 日本語名・英字大小・同名相当のフォルダに対する比較器をファイル名と完全共通化できるかは未実測。

### 3.3 未確定事項との関係

- **FL-003**: grid/listのソート列・方向共有は確定済み。本件はFSフォルダ群への適用規則だけを追加する。
- **IMG-015**: スキャン中はフォルダ/ファイルとも取込順append、完了時に最新sortを適用する契約を維持する。
- **FL-001/002/004**: grid列非搭載、sort状態揮発、表示形式永続範囲は変更しない。

## 4. 是正方針候補(gate①)

### 案A — type-primary + フォルダ名を同方向で独立sort(推奨)

一次キーをtype(`folder`先、`file`後)として固定し、二つの群を個別に整列する。

- folders: 選択列に値がないため**常に名前**で比較し、現在のsort方向だけを共有する。
- files: 現行どおり選択した基本/タグ列と方向で比較する。
- 未sort: 通常ロードは両群とも名前昇順。スキャン中/完了後未sortの取込順保持はIMG-015どおり。
- size/date/tag sort時もfoldersは名前で同方向、folder tile/listに架空の値を表示しない。
- 規模: 小。CAD/BOM契約追加、FS group sorter、unit+VM/headless。DB/schema/OS metadata I/Oなし。
- golden: 複数folder+fileで名前昇降順、size/date/tag列、grid/list往復、パンくず、scan中/完了。

### 案B — フォルダにも列別の集約値を定義

folder size=配下画像サイズ合計、modified_date=配下画像の最大更新日、タグ列=配下画像の集約値等を定義し、
foldersにも選択列と同じ比較を適用する。

- 規模: 大。直下/再帰範囲、空フォルダ、タグ型別集約、表示値、scan中の増分再計算、性能契約が必要。
- DB/schemaは不要でも集約モデル/セル表示/CP-NFRの追加が必要。
- 問題: 利用者要求はfolder metadata/集約値を求めておらず、意味を新設する証拠がない。

### 案C — フォルダは名前昇順固定のまま、group順だけ契約化

現実装を追認し、filesだけをsortする。

- 規模: doc/testのみ。
- 問題: 「フォルダもソート対象」という報告を満たさず、名前降順で両群の方向が食い違うため非推奨。

## 5. 影響BOM(案A採用時)

- CAD: ViewPrismUI `file_list.md`/`image_tab.md`/FL-003 review pointへFS type-primary規則を追加。
- 要求/仕様: REQ-081、`20-spec.md` §2.6へfolder-first+群別sort+IMG-015合成を追加。
- E-BOM: E-UI-BROWSE-022/E-UI-AXIS-NAV-040。
- M-BOM: M-UI-013のFS group sort adapter。`ViewColumnSorter`のファイル比較契約は不変。
- 実装: `ImageTabViewModel`のFS folder/file順序構築。DB/Core/schema/WorkTab/Viewer不変。
- 検査: CP-UI-G1、`CpUiG1ImageTabSelectionTests`へmixed folder/file exact。scan中/完了は既存
  `CpUiEco060ProgressiveScanTests`をread-acrossする。既存Oracle期待値は変更しない(R6)。

## 6. 残ゲート

1. ~~**gate① ViewPrismUI裁定**: 案A/B/Cから選択。推奨は案A。~~ → 案A採用・完了(§7)
2. ~~CAD裁定コミットを製品へ取り込んだ後、`/eco-fix ECO-070`で先行赤probe→是正→機械受入。~~ → 完了(§8)
3. gate② golden: FS軸のfolder-first、群別sort、grid/list、パンくず、scan lifecycleを確認。
4. `/eco-accept ECO-070`でCP/As-Built/register/教訓をクローズ。

## 7. gate①裁定(2026-07-12)

- maintainer裁定: **案A=type-primary + フォルダ名を現在方向で独立sortを採用**。
- FS軸の一次順序は常に`folder群 → image群`。両群を混在させず、個別sort後に連結する。
- folder群は選択列にsize/date/tag値を新設せず、常に名前で比較する。方向だけ現在の`sortDir`を共有する。
- image群は従来どおり選択した基本/タグ列と方向で比較する。架空のfolder値・集約値・OS metadata I/Oは追加しない。
- 未sortの通常表示はfolder名昇順→image名昇順。スキャン中はIMG-015の取込順appendを優先し、
  完了時に明示sortがあれば両群へ最新方向を適用、明示sortなしは既存取込順保持契約に従う。
- タグビュー軸、作業タブ、viewer列、FL-001/002/004は不変。
- ViewPrismUI CAD反映: `0f303a4` (`file_list.md`、`image_tab.md`、FL-003 review point)。
- gate①完了。次の明示入口は`/eco-fix ECO-070`。本裁定ではsrc/testsを変更しない。

## 8. 実施記録(2026-07-12 — 機械受入完了・golden待ち)

### 8.1 先行probe(R5)

- `CpUiG1ImageTabSelectionTests`へ、同階層に`alpha/zeta` folderと`a.jpg/c.jpg`画像を置くmixed fixtureを追加した。
- 初期順=`F:alpha,F:zeta,I:a.jpg,I:c.jpg`、名前降順=`F:zeta,F:alpha,I:c.jpg,I:a.jpg`、
  size降順時もfolder=`zeta,alpha`・同値画像は既存タイブレークで`a,c`を要求した。
- 是正前実測は`ViewPrism2.Tests` **608件中1件不合格(607 pass)**。名前降順の先頭が
  expected=`F:zeta` / actual=`F:alpha`となり、folder群だけ固定昇順の真因を確認してから製品コードへ着手した。

### 8.2 是正裁定とdiff

- `ImageTabViewModel`に`SortFolders`を追加し、明示sort中はfolder名を現在方向で整列する。
  未sort通常時は名前昇順。folderのsize/date/tag値・集約値・OS metadata I/Oは追加していない。
- ECO-060/IMG-015の条件を`TryGetPreservedScanOrder`へ集約し、`SortFiles`と`SortFolders`の両方が
  同じ判定を消費する。scan中と、明示sortなしでscan完了した列は両群とも取込順を保持する。
- `BuildItemsFromMatched`のfolder全件→image全件というtype-primary構造は既存のまま維持し、比較だけを補完した。
- REQ-081、仕様§2.6、E-UI-BROWSE-022、M-UI-IMAGETAB-035、CP-UI-G1へ案Aと潜伏履歴を同期した。
  CADはgate①のViewPrismUI `0f303a4`で同期済み。
- XAML、DB/Core/schema、タグビュー、WorkTab、viewer、i18n、Design System BOM、既存Oracle期待値は変更していない。

### 8.3 機械受入

- 先行probeを含む`ViewPrism2.Tests`: **608/608 pass**(filter指定はMTPで無視されたため全件実行)。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**。既存固定期待値変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: clean。

### 8.4 gate②操作

1. 同じ階層に名前順が判別できるfolderを2件以上、画像を2件以上置き、gridで常にfolder群→画像群となることを確認する。
2. 名前を昇順/降順へ切替え、folder群と画像群がそれぞれ反転し、降順でもfolderが先頭群に残ることを確認する。
3. サイズ・更新日を昇順/降順へ切替え、folder群は名前を同じ方向で、画像群は選択値で並ぶことを確認する。
   folderに架空のサイズ/更新日やソート項目tileが表示されないことも確認する。
4. listへ切替え、同じgroup順・方向を維持し、列header操作後にgridへ戻しても同じ順序になることを確認する。
5. folderへ潜る/パンくずで戻る、FS tag chip絞り込みを行い、各再構築後もfolder先+群別sortを維持する。
6. スキャン中に公開済みfolder/画像が取込順で末尾appendされ、sort方向を変えても完了までは再配列せず、
   完了時に最新の明示sortがfolder/image両群へ適用されることを確認する。
7. タグビュー軸と作業タブにfolder行が混入せず、画像double click後のviewer列が画像だけで画面の画像順と一致することを確認する。
8. タグ編集・作業・整理・削除modeと画像タブのgrid/list仮想化・選択に回帰がないことを確認する。
