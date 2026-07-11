# ECO-064 (implemented) — 大規模コレクションで初期起動をブロックせず読み込み状態を表示する

> maintainer 要求(2026-07-11)を受け、`/eco-file` で工程診断した起動ライフサイクル拡張。
> 起票段階では `src/tests` を変更しない(R1)。

## §1 症状・要求(観測 2026-07-11・報告者 maintainer)

### 観測済みの症状

- コレクションが巨大な場合、アプリの初期起動に時間がかかる。
- 現状は初期データロードが完了するまで利用可能な画面状態にならず、待機中であることも明示されない。
- 壁時計時間、UI threadの無応答区間、画像件数ごとの増加率は未計測。ECO-060/062で約262,046件の実プロファイルが
  存在することは確認済みだが、本ECOの起動所要時間としては未転用する。

### maintainer 要求

- 初期データロード中でもメインUIを先に起動し、UI threadをブロックしない。
- コレクション一覧や画像領域は、利用可能になるまで「読み込み中」であることを表示する。
- 大規模コレクションでも、全件ロード完了をウィンドウ表示の前提にしない。

再現手順:

1. 多数のnormal画像と画像タグを含む巨大コレクションを保存済みprofileに用意する。
2. そのコレクションを前回選択状態にしてアプリを終了する。
3. アプリを再起動する。
4. メインUIの初期表示、コレクション行、選択コレクションの画像が利用可能になるまでを観測する。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **起動時loading状態が未定義** | `docs/screens/image_tab.md` の状態表は未スキャン/スキャン中/スキャン完了を定義するが、アプリ起動時のcollection catalog・選択collection contentのloading/error/retry、操作可否、表示順を定義しない。`docs/review_points.md` の `VP-UI-005` は「空、読み込み中、エラー、無効、選択中などの状態表現」を未確定として明示する |
| 要求・仕様 | **性能/状態契約なし** | REQ-041/仕様§4/CP-NFR-026は大量画像の一覧仮想化・操作時資源を扱うが、shell-first表示、初期ロード中のUI応答、全collection一括取得禁止は要求していない。P-01にもDB取得・集約・VM再構築・初回描画の区分計測は別ECOで扱う旨が残る |
| E-BOM | **状態遷移の欠測** | E-UI-SHELL-021はcollection行、E-UI-BROWSE-022は空状態/仮想化を宣言するが、`uninitialized → loading → ready/error` とcollection catalog/contentの段階境界を持たない |
| M-BOM / Control Plan | **起動経路の欠測** | M-UI-013/M-UI-IMAGETAB-035/M-DB-007は初期ロードの所有・query scope・世代管理を宣言しない。CP-L1-SMOKEは最終的にwindow/画像が表示されること、CP-NFR-026は実体化数を検査するだけで、ロード未完了中のUI可用性や全件repository呼出を検出しない |
| 実装(App/Shell) | **window Openedから単一初期化を開始** | `App.OnFrameworkInitializationCompleted` は `window.Opened` で `MainWindowViewModel.InitializeAsync` をawaitし、同VMは `ImageTab.InitializeAsync` の完了だけを待つ。shell/image tabに起動loading状態や段階公開契約がない |
| 実装(ImageTab/DB) | **collection全体へ比例するeager load** | `ImageTabViewModel.InitializeAsync` は `GetAllNormalAsync` で全collectionのnormal画像、`GetAllImageTagsAsync` で全画像タグを取得し、UI continuation上で辞書化・件数集計後に選択collectionのentriesを構築する。選択collectionだけを表示するにも全collectionの行/タグを先にmaterializeする |

帰属: **既存機能拡張 + CAD/要求/BOMの起動状態契約追加**。現実装は現在のBOMに違反しているとは断定できず、
`VP-UI-005` が明示的に未確定であるため、コードだけの欠陥修正にはできない。ViewPrismUIでloading状態と段階境界を
裁定してから `/eco-fix ECO-064` へ進む。

未確定事項との関係:

- `VP-UI-005` に直接該当する。本ECOで画像タブ起動に必要な範囲を確定し、全画面共通loading表現への一般化は別裁定に残せる。
- ECO-060/IMG-015の「スキャン中」はデータ製造中の状態であり、本件の「永続済みデータを起動時に読むloading」とは別状態。
- ECO-058/FMEA-013の仮想化は描画実体数を抑えるが、DBから全normal画像/全画像タグを取得するeager loadを抑えない。

## §3 切り分け済みの事実

### 確定(コード・CAD・BOM・履歴で確認)

- MainWindowは先に生成されるが、`Opened`から始まる初期化には`IsLoading`等の公開状態がなく、XAMLは未ロード状態と
  「collection未選択/画像0件」の空状態を区別できない。
- `ImageTabViewModel.InitializeAsync` の全normal画像取得・全画像タグ取得・in-memory件数集計は
  `6f7b4f9`(2026-06-18、画像タブM3a初版)から存在する。`App`のOpened初期化は`1446321`から存在する。
- 全normal画像はcollection件数表示の集計にも使われ、選択collectionの表示entriesにも再利用される。この共有により
  catalogの件数取得とcontentロードの境界が分離されていない。
- repositoryにはcollection単位の`GetByFolderAsync`があるが、起動経路は`GetAllNormalAsync`を用いる。
  画像タグには起動用のcollection単位取得APIがなく、`GetAllImageTagsAsync`で全件を読む。
- `ConfigureAwait(true)`後の辞書化、GroupBy、BuildEntries/RecomputeはUI contextへ戻るため、少なくともCPU側の全件処理は
  UI thread上で行われる構造である。
- CP-NFR-026は仮想化・再構築・タグ列O(1)を検査するが、起動時repository呼出scopeとloading状態をpinしない。

### 疑い(未検証 — `/eco-fix` のプローブで測る)

- DB query/materialization、全画像タグ辞書化、件数集計、選択collection VM構築のどれが支配的かは未計測。
- `Microsoft.Data.Sqlite`/Dapperの非同期呼出が実profileでUIへ制御を返す区間と、同期的に進む区間は実測が必要。
- collection catalogを集約count query、画像/contentを選択collection queryへ分離すれば、初期可用時間とメモリは
  全collection総件数ではなくcollection数+選択collection件数へ限定できる見込みだが、プローブで呼出数と状態遷移を固定する。
- 初期ロード中のtab切替、collection再選択、終了が競合した場合のキャンセル/世代無効化要否はCAD裁定後にprobeする。

## §4 是正方針(gate①裁定済み — 2026-07-11)

maintainerが2026-07-11に**案A**を採用した。CADはViewPrismUI `84d1e2d`
(`decide(IMG-019): 起動をshell-first二段loadingへ`)で先行改訂済み。`docs/screens/image_tab.md`を挙動正本とし、
VP-UI-005は画像タブ起動に必要な範囲だけ部分決定、全画面共通component化は継続課題として本ECOへ混ぜない。

### 案A(採用): shell-first + catalog/content二段ロード + 選択collection限定

1. window/shellを先に描画し、collection catalogと画像contentに別々のloading状態を持たせる。
2. catalogはcollection行とnormal件数の集約だけを取得し、全ImageRecordを件数計算のためにmaterializeしない。
3. settingsの選択collectionを解決後、そのcollectionのnormal画像と画像タグだけを取得して中央contentを公開する。
4. collection切替中は旧contentを新collectionの結果として見せず、loading表示+世代/cancellationで遅延結果を破棄する。
5. catalog準備後はcollection選択を可能にし、選択contentのロード中もshell/tab/設定操作を応答可能に保つ。

- diff規模: **中〜大**。CAD+REQ/spec+E/M-BOM、repository集約/count・collection単位tag API、MainWindow/ImageTab状態、XAML、
  非同期世代管理、unit/headless/golden harnessを変更。
- golden影響: CP-UI-G1でcold start中のwindow操作、catalog loading→ready、選択content loading→ready、別collectionへの
  連打/戻り、エラー表示を確認。巨大profileでも全件完了を待たずshellが応答することを確認。
- 利点: 総collection件数比例のeager loadを除去し、今回の性能原因そのものへ上限を置ける。

### 案B(不採用): monolithic eager loadをbackground化 + 全体loading overlay

- 現在の`GetAllNormalAsync`/`GetAllImageTagsAsync`/全件集約は維持し、単一初期化をUI thread外へ移して完了まで画像tab全体を
  loading overlayにする。
- diff規模: **小〜中**。CAD状態、MainWindow/ImageTabのloading/error、thread境界、XAML、probeを変更。
- golden影響: windowが先に表示され、loading中も移動/再描画可能、完了後に現行状態へ一括遷移することを確認。
- 欠点: 起動時間・ピークメモリ・DB処理量はcollection総件数比例のまま。単一共有SQLite接続との並行操作を原則禁止する
  必要があり、「collectionなどが読み込み中」の段階利用性は得にくい。暫定策としてのみ妥当。

共通不変条件:

- loadingは「未選択」「0件」「未スキャン」「スキャン中」と別状態・別文言にする。
- 初期化失敗はGlobalExceptionHandlerへ投げて空状態へ黙って化けさせず、再試行可能なerror状態を表示する。
- 遅延完了が新しいcollection選択を上書きしない。終了後にUI更新しない。
- settingsのLastCollectionId復元、home view初期遷移(ECO-063)、scan batch段階公開(ECO-060)、grid/list仮想化(ECO-026/058)を維持する。
- 壁時計の単独閾値をunit合否にせず、repository呼出scope、状態遷移、UI dispatcherを占有しないwork境界を決定論的に検査する。
  実規模の知覚応答は隔離profile+goldenで受ける。

## §5 影響BOM / 受入計画

- ViewPrismUI `docs/screens/image_tab.md` / `docs/review_points.md VP-UI-005` / integrated spec
  - app startup、catalog loading、content loading、error/retry、操作可否、表示優先順を裁定。
- `10-requirements.yaml` / `20-spec.md`
  - 新REQ: shell-first可用性、ロード中状態、選択collection限定データ取得、遅延結果破棄。
- `E-UI-SHELL-021` / `E-UI-BROWSE-022`
  - `uninitialized → loading → ready|error`、catalog/content二段境界、空状態との排他。
- `M-UI-013` / `M-UI-IMAGETAB-035` / `M-DB-007`
  - 初期化owner、loading state、collection限定query、aggregate count、generation/cancellation、thread境界。
- `CP-NFR-026` + 新規startup CP / `CP-UI-G1` / `CP-L1-SMOKE`
  - 全collection image/tag materialization禁止、selected scope exact、loading遷移、遅延結果破棄、UI応答golden。
- 新規FMEA + `P-01` / `M-GOLDEN-HARNESS-039`
  - eager全件取得で起動不能、loadingと空状態混同、旧ロード結果の上書きをfailure mode化。
  - 隔離大規模fixtureでDB取得/集約/VM構築/初回描画を区分観測する。実profileや私有データは受入証跡に使わない。

既存固定Oracle行は変更しない(R6)。新規unit/headless/probeを追加する。

## §6 残ゲート

1. ~~**gate① ViewPrismUI裁定**: 案A / 案B、loadingの表示範囲、error/retry、loading中に許可する操作を確定する。~~
   → **案A採用**(2026-07-11・maintainer)。
2. ~~CAD commitを先行し、製品ECOへ取り込む。~~ → ViewPrismUI `84d1e2d`で完了。
3. ~~/eco-fix ECO-064: プローブ先行で現行eager scope/UI-thread境界を不合格化してから製造する。~~ → §7で完了。
4. ~~機械受入: build 0 / Tests / Oracle / validate_bom 0-0 / lifecycle。~~ → §7で完了。
5. gate② golden: 隔離大規模profileでshell-first、loading状態、応答性、ready/error遷移、既存復元回帰を実機確認する。

## §7 実施記録(2026-07-11 — 是正+機械受入完了)

### 7.1 プローブ先行(R5)

- `CpUiG1CollectionScopeTests`へ起動時catalog/content loadingプローブを先に追加。
- 是正前実測: `ImageTabViewModel`に`IsCatalogLoading`/`IsContentLoading`/error状態が存在せず、
  **CS1061 6件で不合格**。未ロードと空状態を区別できない診断を実測で確定した。
- 是正後はECO-064 probeを5本へ拡張:
  1. Initialize直後のloadingとprompt/empty排他、完了後ready。
  2. startup/切替で`GetAllNormalAsync`/`GetAllImageTagsAsync`呼出0、catalog集約1回、A/B folder id exact。
  3. catalog fail→error(偽promptなし)→retryでcatalog ready。
  4. content fail→error(偽0件なし)→retryで同collection ready。
  5. A遅延中にB ready→A完了後もBの選択/items不変。

### 7.2 是正

- Repository/Core interface:
  - `GetNormalCountsByFolderAsync`: SQL `GROUP BY sync_folder_id`でnormal件数だけを返す。
  - `GetNormalByFolderAsync`: 選択collectionのnormal画像だけ。
  - `GetImageTagsByFolderAsync`: images JOINで選択collectionかつnormalのimage_tagsだけ。
  - `CountByFolderAndStatusAsync`: ゴミ箱badge等も画像行materializeなしの集約へ変更。
  - 既存全件APIはOracle/後方互換consumer向けに維持するがstartupからは呼ばない。
- `ImageTabViewModel`:
  - `InitializeAsync`はcatalog loadingを同期公開→`Task.Yield`でshell初回描画を許可。
  - catalog/contentのDB query/materializeを`Task.Run`明示境界へ分離(`ConfigureAwait(false)`だけに依存しない)。
  - catalog ready時はcontent未readyでもcollection行だけを公開。contentは保存済み/選択collectionだけを読む。
  - catalog/content別loading/error+retry、generation+CTS、window closingの`CancelLoading`を実装。
  - content snapshotはimages/image_tags/tag定義/view定義/deleted countをbackground取得し、世代照合後に原子的に公開。
- `ImageTabView.axaml`: 左catalog領域と中央content領域に別々の「読み込み中…」/error/「再試行」を追加。
  未選択/0件/未scan/scan中とは`IsVisible`を排他化。shell全体overlayは置かない。
- BOM: REQ-088、E-UI-SHELL/BROWSE、M-DB/M-UI、CP-STARTUP-028、FMEA-040、Service BOMを同期。
- 変更なし: DB schema/migration、CAD(ViewPrismUIはgate① `84d1e2d`で先行済み)、画像/タグの意味論、
  settings schema、scan/pHash/merge/thumbnail、既存固定Oracle行。

### 7.3 機械受入

- `dotnet build --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Tests`: **590/590 pass**(既存585 + ECO-064新規5)。
- `ViewPrism2.Oracle`: **109 pass + 既知2 skip**、既存固定行無改変(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: errorなし。

### 7.4 gate② golden手順

1. 隔離した大規模profileでアプリをcold startし、shell(タブ/設定)が先に表示されwindow移動/resize/タブ往復が応答する。
2. 左に「コレクションを読み込み中…」→行+件数、保存済みcollectionがあれば中央に「画像を読み込み中…」→画像、
   の順で遷移する。途中に「選択してください」「画像がありません」を誤表示しない。
3. content loading中に別collectionを選び、後から旧loadが完了しても現在選択/画像が戻らない。
4. 読み込み完了後、LastCollection復元、FS/view軸、home初期遷移、grid/list、scan中表示が回帰しない。
5. error/retryは通常実機で故意にDBを破壊しない。機械probeで承認し、goldenは文言/配置/通常ready遷移を確認する。
