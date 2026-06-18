# Change Order — ECO-013(画像タブ 原典撤去・前提作業)

> 画像タブ製造 M3(実データ二軸ブラウズ + インライン付与 + 連番)完了後、画像タブは **harness 併走**(右上 harness チェック ON=新 ImageTabView / OFF=原典 MainWindow Grid)の状態。原典 surface を撤去して **ImageTabView 一本化**するには、精査の結果 2 つの前提作業が必要と判明した(M3 繰り越し task #5)。本 ECO はその前提作業を spec-first(CAD→UI-IR→E-BOM)で正式化し、製造で適用する。
> **帰属: design_decision(2 件・maintainer 裁定 2026-06-18)+ surface 統合**。本 ECO は **起票 + CAD 形式化(ViewPrismUI)+ E-BOM(30)同期** 段階。実装(REQ-053 保全・管理入口配線・テスト移行・原典撤去)は製造で適用。spec(20)/M-BOM(32)/Control Plan(33) の全面同期は後続 **M4**。

## 0. 変更前 baseline
- As-Built: commit `d63a683`(画像タブ製造 M3c)。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- ベースライン実測(2026-06-18): `dotnet test tests/ViewPrism2.Tests` 緑(exit 0)・`tests/ViewPrism2.Oracle` 緑(exit 0)。
- 本 ECO は **surface 統合 + 表示/挙動契約**(スキーマ・固定オラクル不変)。

## 1. 変更要求
- ECO-ID: **ECO-013**
- 発生契機: 画像タブ原典 surface 撤去の精査で、撤去が単純削除でないことが判明(M3 繰り越し)。
- 内容: 撤去の 2 前提(a/b)を CAD/spec へ正式化し E-BOM を同期。
- 種別: **設計決定 + surface 統合**。

## 2. 撤去をブロックしていた 2 前提(実コードで実証)
harness 構成: `MainWindowViewModel.ShowImageTabPreview`(既定 ON)→ `ShowImageTabHarness`(新 ImageTabView)/ `ShowImageTabLegacy`(原典 Grid `MainWindow.axaml:160-433`)。harness チェック `MainWindow.axaml:129-132`。

| # | 前提 | 実態(撤去前) |
|---|---|---|
| (a) | フォルダ管理入口 | 新 ImageTabView の左サイドバーは**ブラウズ専用**。原典が持つ「管理」(`OpenFolderManagementCommand`)・行別スキャン(⟳ `ScanCommand`)・「フォルダを追加」(`AddFolderCommand`)が新 surface に無い。撤去すると追加/スキャン/管理の導線が消える |
| (b) | REQ-053 永続化・未選択挙動 | 新 `ImageTabViewModel` は init 時に `preferredCollectionId` を受け取るが、**選択時の書き戻しなし**・**表示モード永続化なし**(`Browser.IsListMode` を触らない独自 `_layout`)・**未選択プロンプト/消失フォールバックなし**(先頭コレクション自動選択)。CR-5/CR-6 と未選択空状態は現状シェル側 legacy メンバ(`SelectedCollectionId`・`CaptureSettings()`)が担っており、撤去すると golden 承認済 REQ-053(コレクションファースト)が退行する。CP-UI-G1 の `CpUiG1CollectionScopeTests` は原典 `MainWindowViewModel` 契約に依存 |

## 3. disposition(maintainer 裁定 2026-06-18)
### 決定(2件)
| ID | 決定 | 根拠 | 同期先 |
|---|---|---|---|
| **(a) 管理入口** | **CAD 先行設計 → モック既存の「追加(+)」を正式化**。`+`(展開ヘッダ + 折り畳みレール)= 既存 `FolderManagementWindow`(追加/スキャン/削除)を開く**単一入口**。原典の行別⟳/独立「管理」導線は本入口へ集約 | モック `ViewPrism2 画像タブ.dc.html` に `+` は既に**視覚として存在**(`:117` ヘッダ・`:103` レール)し onClick 未配線だっただけ=net-new UI でなく**既存アフォーダンスの正式化**。コレクション行は閲覧に専念しクロムを静かに保つ(コア方針)。既存 UI 再利用で実装最小 | ViewPrismUI `docs/screens/image_tab.md`(コレクション管理節 + インタラクション + IMG-009)/ E-UI-SHELL-021 |
| **(b) REQ-053** | **完全保全(等価維持)**。新 `ImageTabViewModel` に LastCollectionId/DisplayMode の書き戻し・復元、未選択プロンプト、保存コレクション消失時の未選択フォールバックを実装。`CpUiG1CollectionScopeTests` を新 VM 契約へ移行 | golden 承認済 REQ-053(コレクションファースト・横断なし)を退行させない。挙動を原典と等価維持し、surface のみ ImageTabView へ統合する | E-UI-SHELL-021 / E-UI-GRID-022 / CP-UI-G1 |

### 撤去対象(製造で適用)
- `MainWindow.axaml`: 原典画像タブ Grid(`160-433`)・harness チェックボックス(`129-132`)。
- `MainWindowViewModel`: harness トグル(`ShowImageTabPreview`/`ShowImageTabHarness`/`ShowImageTabLegacy`)+ legacy 画像タブメンバ(画像タブ用 `Browser`・`Recents`/`Favorites`/`TreeRoots`/`SelectedCollectionId`/`Show*Pane`/`ShowCollectionPrompt`/`ShowEmptyMessage` 等・`SelectCollectionCommand`/`SelectViewListItemCommand` 等の画像タブ用ロジック)。
- **温存**: `FolderManagementViewModel`(=`FolderPane`)は `FolderManagementWindow` で継続利用。タグタブと共有するメンバは撤去前に依存検証する。

## 4. BOM 改訂(本 ECO で同期)
### CAD(ViewPrismUI)
- `docs/screens/image_tab.md`: 「コレクション管理」節新設(`+` = 管理ビュー起動・追加/スキャン/削除・非破壊)+ インタラクション表に「コレクション追加 (+)」行 + モック差分の記録(VP-UI-006)。
- `docs/review_points.md`: **IMG-009** 追加(決定済)。

### E-BOM(`30-ebom.yaml`)
- `E-UI-SHELL-021`: **(ECO-013/IMG-009)** コレクション「追加(+)」= 管理ビュー単一入口(行別⟳/独立管理を集約・非破壊)。**(ECO-013)** surface 統合に伴い REQ-053(コレクションファースト)・REQ-052 CR-5/CR-6 永続化/復元・未選択プロンプト・消失フォールバックを新 `ImageTabViewModel` が担う(等価維持)・CP-UI-G1 を新 VM 契約へ移行。
- `E-UI-GRID-022`: **(ECO-013)** グリッド/リストは新 ImageTabView が単独描画(原典重畳 Grid・harness 撤去)。選択ロジック unit 検査点が ImageTabViewModel へ移る。
- 固定オラクル: **追加なし**(S-01〜S-31 不変)。スキーマ不変。

### 製造時/M4 同期(後続)
- `20-spec.md` / `32-mbom.yaml` / `33-control-plan.yaml`: surface 統合・管理入口・REQ-053 保全の反映は **M4** で全面同期。

## 5. 受入(計画)
- (a): `+`(展開/折り畳み両方)→ FolderManagementWindow が開き、追加/スキャン/削除後にコレクション一覧・件数・画像が反映される(golden + 機能確認)。削除でコレクション解除・実ファイル非破壊(INV-009)。
- (b): `CpUiG1CollectionScopeTests` 相当を新 `ImageTabViewModel` 契約で緑(未選択プロンプト・母集合=選択コレクション status=normal・画像数・NodeGraph 評価 scope・LastCollectionId/DisplayMode 永続化と復元・消失フォールバック)。
- 撤去後: 画像タブが ImageTabView 一本で動作(harness/原典 Grid なし)。
- 回帰: `dotnet test tests/ViewPrism2.Tests` + `tests/ViewPrism2.Oracle`(S-01〜S-31)退行ゼロ・build 警告0。実機 golden(新 surface 一本化・+管理入口・REQ-053 復元)。

## 6. provenance / lesson 連結
- 本 ECO は M3 繰り越し(task #5)を spec-first で正式化したもの。撤去が「単純削除」でなく「機能移行(管理入口)+ 挙動保全(REQ-053)」を伴うことを撤去前に可視化し、ECO 連鎖((c) gap)で扱った(直接ハンドコードしない・ECO-003 で institutional 化した所見トリアージ)。
- lesson: 「harness 併走 → 原典撤去」は、新 surface が原典の**機能(管理入口)と契約(REQ-053 永続化/未選択)を等価に引き受けられるか**を撤去前に検証すべき。CAD を読み直すと管理入口(`+`)は既にモックに設計されており(抽出漏れ)、「ゼロから新規設計」でなく**既存 CAD アフォーダンスの正式化**で済んだ=read-across(モック→実機)を撤去設計でも行う価値。

## 7. golden 第1回(2026-06-18・maintainer 実機・harness ON)— 所見と裁定
撤去前 golden で、新 surface が **M1–M3 で「タグ編集/ブラウズ設計」を golden ハーネス化した範囲**に留まり、原典の一部挙動を deferred のままにしていたことが判明(撤去をブロック=golden-in-the-loop の有効性)。

| # | 所見 | 区分 | 是正 |
|---|---|---|---|
| 1 | 管理入口(`+`) | OK | — |
| 2 | リスト→再起動でグリッドに戻る | bug | harness ON 中、原典 `MainWindowViewModel.CaptureSettings` が legacy `Browser.IsListMode` で新 VM の値を上書き(App 終了時)→ **CaptureSettings を `ImageTab` へ委譲**(DisplayMode/LastCollectionId は ImageTab が所有) |
| 3 | 未選択プロンプト | OK | — |
| 4b | SHIFT 連続選択が歯抜け | bug | `ToggleSelect`/ビューアーの母集合 `AllLoadedImagesInContext()` が**未ソート**で表示順とズレ → **表示順(SortFiles)に統一**。回帰テスト追加 |
| 4e | 閲覧ダブルクリックでビューアー起動せず | 機能欠落 | 閲覧モードのダブルクリック=`IWindowService.ShowViewer`(表示順)を配線(DoubleClickDetector で DF-4 堅牢化) |
| 4d | ホバーで背景色なし | parity | グリッドセルにホバー淡塗り(`thumbCellWrap:pointerover`・エクスプローラー同様) |
| 5a | 折り畳みコレクションのクリックで切替わらない | bug | railIcon `Button` が PointerPressed を Handled 化し code-behind が発火しない → **`Command` バインドへ変更**(`SelectCollectionCommand`) |
| 5b | 展開コレクションカードの幅が内容依存(長いパス NoWrap がカード desired 幅を駆動しビューポートを超えオーバーフロー→件数バッジがサイドバー端でクリップ) | bug | カード幅を `ScrollViewer.Viewport.Width` に束縛 + 横インセットを Padding→Margin(Viewport 自体を縮め垂直スクロールバーにも自動追従)。`HorizontalScrollBarVisibility=Disabled`+`Stretch` は ItemsControl をビューポート幅に拘束せず無効だった。実機キャプチャ(PrintWindow)で均一幅・バッジ全表示を確認(別コミット 480ec5b) |
| **4a/4c** | 選択視覚=チェック(原典=①番号順) | **design 差し戻し** | **maintainer 裁定: 連番順の番号バッジを復活**(原典準拠)。**UQ-I12(モック=チェック)を golden 実機検証で差し戻す**(GF 所見)。連番(ページ番号)付与に選択順の可視化が必須。`ImageItemVM.SelectionOrder`(1 起点)+ thumbCheck を番号表示へ |
| **4f** | 閲覧クリックで選択 | **design 確認** | **maintainer 裁定: 選択はタグ編集モード専用(モック準拠)**。閲覧モードのシングルクリックは無操作(モード分離)。ダブルクリック=ビューアーのみ |

- 検証: build 0 警告 / Tests 410(+4 回帰=選択順・SHIFT 表示順・閲覧クリック無操作・閲覧ダブルクリック起動) / Oracle 74+2skip(S-01〜S-31 不変)。
- lesson: 「golden ハーネス surface」は設計範囲(タグ編集/ブラウズ)を忠実化する一方、**原典の周辺挙動(ビューアー起動・選択順・ホバー・閲覧操作)が暗黙に脱落**しうる。撤去前 golden は「新 surface=原典の完全な機能等価か」をモード横断で突合する関門。UQ-I12 のように**モックの静的決定が実ワークフロー(連番)で破れる**ケースは実機 golden でのみ捕捉でき、差し戻し(GF)で是正する。
