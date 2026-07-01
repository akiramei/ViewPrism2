# ECO-024: 画像タブ 原典(legacy)surface 撤去 — ImageTabView 一本化(as-built 実態化)

- **status**: implemented(コード撤去+テスト移行を実施)。検証: build 0警告 / Tests(移行後) / Oracle 100+2skip / validate_bom 0 error
- **type**: surface 撤去(as-built 実態化)。要件・不変条件・Core 意味論は不変。E-BOM が ECO-013 以降予告してきた「新 ImageTabView 一本化・原典撤去」の実現
- **baseline**: ECO-023 適用後(main `d09b1e6`)
- **bom_rev**: v4.0(eco:ECO-024)
- **前提(全ブロッカー解消済)**: REQ-043(詳細/ノート)は ECO-023 で撤回=原典撤去の最後のブロッカー解消。UI-IR 権威は ViewPrismUI 単一化(P3)。新 surface `ImageTabViewModel`/`ImageTabView` は ECO-013〜022 で機能完成・golden 承認済

## 1. 背景 — なぜ今撤去できるか

画像タブは M3 以降、**新 surface `ImageTabView`(`ImageTabViewModel`)を実データで併走(harness)**させ、原典(legacy)MainWindow 画像タブ Grid と併存してきた。E-UI-SHELL-021 / E-UI-BROWSE-022 の invariants は ECO-013 時点で「新 ImageTabView 一本へ統合し原典 Grid・harness・legacy 実装を撤去する」と既に規定済(実行は残ブロッカー解消まで保留)。

残ブロッカーは順次解消:整理=ECO-014 / ⋯=ECO-015 / 作業=ECO-017 / 削除=ECO-018 / ゴミ箱=ECO-019 / 作業タブ=ECO-020/021 / **詳細・ノート=ECO-023 で撤回(最後のブロッカー)**。よって本 ECO で原典撤去を実行する。

## 2. 依存検証(撤去前・ECO-013 教訓=挙動突合だけでは機能欠落を捕捉できない)

新 surface が原典と機能等価(むしろ上位)であることを確認済:

| 機能 | 原典(legacy) | 新 surface `ImageTabViewModel` | 判定 |
|---|---|---|---|
| コレクション選択スコープ REQ-053 | MainWindowVM `SelectedCollectionId`/`LoadBaseDataAsync` | `SelectCollection`/`BuildEntries`(ECO-013 で移管・等価維持) | 等価 |
| 表示軸ナビ | 左 NodeGraph ツリー(`TreeRoots`) | FS/view 軸チップ+パンくず(ECO-010/011) | 上位 |
| ソート/グリッド・リスト | `Browser` | `SelectSort`/`SetGrid`/`SetList` | 等価 |
| タグ編集 REQ-046 | `IsTagEditMode`+`TaggingPanelViewModel` | inline `AddGroups`/`CurrentTags`/`ApplyTag*`(ECO-010) | 等価 |
| ビューア起動 REQ-041/044 | `Browser.OpenItemRequested`→`OpenViewer` | `OpenViewer`(ダブルクリック・表示順) | 等価 |
| 類似/マージ | `SearchSimilarCommand`/`MergeSelectedCommand`(独立モーダル) | 整理トレイ(ECO-014) | 上位 |
| トラッシュ/修復 | `OpenTrashCommand`/`OpenRepairCommand`(モーダル) | ⋯メニュー+in-tab ポップアップ(ECO-015/018/019) | 上位 |
| 削除/作業 | (無し) | 削除モード(ECO-018)/作業モード(ECO-017/020) | 新規 |
| 詳細パネル/ノート REQ-043 | 右パネル `Detail`(`DetailPanelViewModel`) | **後継なし**(ECO-023 で撤回) | 撤回済 |
| 設定永続化 CR-5/CR-6 | MainWindowVM `CaptureSettings` | `ImageTab.CaptureSettings` | 等価 |

共有メンバの他画面参照検証:
- `FolderPane`(シェルの `FolderManagementViewModel`)/`Tagging`(`TaggingPanelViewModel`)は **legacy Grid 専用**。フォルダ管理モーダルは `WindowService` が別インスタンスを生成(`FolderManagementViewModel` クラスは存続)。
- `TreeRoots`/`GraphNodeViewModel`/`ViewListItemViewModel`/`ImageItemViewModel` は legacy チェーン専用。**タグタブは `GraphNodeViewModel` を共有しない**。
- 作業タブは inline タグ編集で `TaggingPanelViewModel` を再利用しない(ECO-021 β-2 は E-UI-TAGASSIGN-029 の意味論を再利用するが末端 VM は独立)。
- `DoubleClickDetector` は新 surface `ImageTabView.axaml.cs` も使用=**存続**(撤去しない)。

## 3. 撤去対象(確定リスト)

### コード
| ファイル | 撤去内容 |
|---|---|
| `Views/MainWindow.axaml` | legacy 画像タブ Grid(3ペイン・239-512)全体、harness トグル CheckBox(183-186)、`ImageTabView` の `IsVisible` を `ShowImageTabHarness`→`IsImagesTabSelected` へ |
| `Views/MainWindow.axaml.cs` | legacy code-behind(`OnTreeSelectionChanged`/`OnViewItemPressed`/`OnCellPressed`/`OnContentSizeChanged`/`OnCollectionPressed`/`SelectedTreeNode` 同期/`DoubleClickDetector` 併走)。`DoubleClickDetector` クラス自体は新 surface が使うため残す |
| `ViewModels/MainWindowViewModel.cs` | legacy 画像タブ・パイプライン全体(`Browser`/`Detail`/`Tagging`/`FolderPane`(シェル)/`Favorites`/`Recents`/`AllImagesItem`/`TreeRoots`/`SelectedCollectionId`/`SelectedTreeNode`/`SelectedViewItem`/`IsTagEditMode`/harness メンバ/legacy pane 計算/`ReloadAsync`/`SelectViewItemAsync`/`EvaluateAndShow`/`LoadBaseDataAsync`/`ReloadViewListsAsync` 等+関連コマンド)。**薄いシェル**(タブ切替・ImageTab/WorkTab/TagsTab ホスト・ローカライズ・設定確定)へ減量 |
| `App.axaml.cs` | MainWindowVM ctor から `FolderManagementViewModel`/`TaggingPanelViewModel`/`ThumbnailService` 引数を除去。両 VM の DI 登録は MainWindowVM 専用のため除去(`FolderManagementViewModel` は `WindowService` が直接 new するため DI 登録不要) |
| **削除ファイル** | `ImageBrowserViewModel.cs`(内包: `LabeledOption`/`SortFieldOption`/`SortDirectionOption`/`GridRowViewModel`/`ListColumnViewModel`/`ListCellViewModel`/`ImageItemViewModel`)・`DetailPanelViewModel.cs`・`ViewListItemViewModel.cs`・`GraphNodeViewModel.cs`・`TaggingPanelViewModel.cs`・`Views/TaggingPanelView.axaml(.cs)` |

### 残すもの
`ImageTab`/`WorkTab`/`TagsTab`/`ImageTabView`/`WorkTabView`/`TagsTabView`/`DoubleClickDetector`/`FolderManagementViewModel`(モーダル用)/`ImageTab.ReloadTagCatalogAsync` 経路(タグタブ永続変更→画像タブ反映)。

## 4. テスト移行(maintainer 裁定: 移行を確認してから削除)

各 legacy テストの [Fact] カバレッジを新 surface / Core テストと突合し、**挙動カバレッジ**(実装機構でなく)を基準に処置:

| legacy テスト | 処置 | 根拠 |
|---|---|---|
| `CpTagUi013Tests`(8件・`TaggingPanelViewModel`) | **削除** | 共通タグ/連番選択順/固定値/範囲外拒否/原子ロールバック/解除/textual/プレースホルダは Core `TagService`(CpTag011)+ 新 surface タグ編集経路で全 COVERED |
| `CpDisplayParity022Tests` A-2(`ImageBrowserViewModel`.SizeText) | **移植** | `ImageTabViewModel.Items[*].SizeText`(同一 `ImageItemVM`)へ差し替え。A-1/3/4/5/6 は別 VM で不変 |
| `CpL1SmokeTests`(MainWindowVM 経由 `Browser`/`Detail`) | **移植** | CP-L1-SMOKE 統合スモークを新 surface(`ImageTab.InitializeAsync`→`Items`→`OpenViewer`)経由へ再実装。ビューア経路(V2)は独立 `ViewerViewModel` で不変 |
| `CpUiG1SelectionTests`(`ImageBrowserViewModel` + `DoubleClickDetector`) | **分割** | 選択挙動(クリック/Ctrl/Shift/ダブルクリック→ビューア)は `CpUiG1ImageTabSelectionTests` で COVERED。`DoubleClickDetector` テストは存続クラスをテスト=別ファイルへ退避し保全。列計算(`ComputeColumns`/`CellSize`)・レスポンシブ・ソート選択肢は legacy 実装固有機構=新 surface は view 層+golden(CP-UI-G1)で担保するため**移行先が存在せず削除**(挙動喪失ではない) |

## 5. 影響 BOM(as-built 実態化。全面 prose 同期は M4)

| 対象 | 是正 |
|---|---|
| E-UI-SHELL-021 | ECO-013 invariant「新 ImageTabView 一本へ統合・原典撤去」を as-built 実現(注記を実施済へ)。CP-UI-G1(REQ-053)は新契約 |
| E-UI-BROWSE-022 / E-UI-AXIS-NAV-040 / E-UI-MODE-041 | 描画・選択・モードの単独 realization=`ImageTabView`/`ImageTabViewModel` に確定(legacy Grid as-built 撤去) |
| M-UI-013(シェル VM) | artifact を legacy Grid 撤去後の実態へ。interface_contract の v1.2 prose(左 NodeGraph ツリー/右詳細⇔タグ付与切替)を新 surface 実態へ同期(M4)。acceptance CP-L1-SMOKE を新 surface スモークへ |
| M-UI-016(`TaggingPanelViewModel`) | 末端 VM 撤去。E-UI-TAGASSIGN-029 の realization を `ImageTabViewModel`/`WorkTabViewModel` の inline タグ編集へ実態化。CP-TAGUI-013 は Core `TagService` + 新 surface へ再帰属(M4) |
| CP-UI-G1 / CP-TAGUI-013 / CP-L1-SMOKE(33-control-plan) | 検証点を新 surface / Core へ移行(M4 で全面同期) |
| 60-change-register.yaml | ECO-024 エントリ追加 |

## 6. 検証

- `dotnet build src/ViewPrism2.App/ViewPrism2.App.csproj -c Debug`: 0 警告
- `dotnet test`(Tests): legacy テスト削除+移行後に緑(件数は移行で変動・報告に記録)
- Oracle: 100+2skip 不変(Core 意味論不変)
- `validate_bom.py`: 0 error(削除ファイル参照の dangling が無いこと)

## 7. M4 同期(実施済)

ECO-024 スコープの as-built 同期を実施(`validate_bom` 0 error / 0 warning):

- **32-mbom** M-UI-013 interface_contract を薄いシェル+ImageTabView 一本化へ同期(撤去済 legacy を明記・表示軸/モードの権威を E-UI-BROWSE/AXIS/MODE へ)。M-UI-013 acceptance から retired の CP-UI-G3 を除去。M-UI-016 を `TaggingPanelViewModel` 撤去→`ImageTabViewModel`/`WorkTabViewModel` インライン実態化。
- **新 M-UI-WORKSPACE-029**(E-UI-WORKSPACE-043 の製造トレース)を追加=**W2 解消**。
- **33-control-plan** CP-TAGUI-013 を Core `TagService`(CpTag011)+ 画像タブ インライン付与へ再帰属。CP-UI-G1 は ECO-016 で既に新 surface 記述のため据え置き。
- **20-spec §2.6** 画像タブ節に as-built 注記を追加、撤去済の左 NodeGraph ツリー・詳細パネル記述を是正。

**継続課題(本 ECO スコープ外)**: ECO-010〜021 の evolved design(表示軸チップ+パンくず・整理/作業/削除/ゴミ箱の逐条)の 20-spec §2.6 全面 prose 同期。当面は E-UI-BROWSE-022 / E-UI-AXIS-NAV-040 / E-UI-MODE-041 + ViewPrismUI UI-IR が権威。
