# ECO-021(ECO-β): 作業タブ 右ペイン文脈モードの再利用配線(段階 β-1〜β-4)

- **status**: implemented(β-1〜β-4 全段製造済 = ECO-β 完了)+ レビュー是正3件
- **検証**: App build 0 警告 / Tests 454(CpUiG1WorkTabTests 計9件)/ Oracle 74+2skip(S-01〜S-31 退行ゼロ)
- **type**: 既存 surface の再利用配線(CADモック由来)— 新 Core 意味論なし(Core サービスは既存を再利用)
- **baseline**: ECO-020(ECO-α)適用後
- **bom_rev**: v4.0(eco:ECO-021)
- **cad_input**: `ViewPrismUI:資料/画像タブ/ViewPrism2 作業タブ.html`
- **UI-IR/UI-BOM**: `bomdd/ui/work-tab/`

## 0. 再利用方針(UQ-W06 決定 2026-06-29 maintainer)

**B = 作業タブ側でオーケストレート(隔離方式)。** 画像タブ(`ImageTabViewModel`/`ImageTabView`)は collection/axis 結合かつ **golden 済み・原典撤去の本線**のため触れない。`WorkTabViewModel` が **Core サービス(TagService/Similarity/Merge/Trash/Criteria)・末端 VM(ImageItemVM/ChipVM/CurrentTagVM/AddRowVM/OrganizeSlotVM/TrashPopupItemVM)・DS(Components.axaml)を再利用**してモード機構を自前で組む(モックの共有 DCLogic と同型)。VM オーケストレーションは重複するが、両 surface が golden 済み・原典撤去後に共通部品へ DRY リファクタ可能(isolate now, DRY later)。**A(画像タブを scope 多態化して共有)は golden 退行リスクと撤去の絡みで不採用。**

## 1. 段階(UQ-W05=段階分割の β 内訳)

| 段階 | 内容 | 状態 |
|---|---|---|
| **β-1** | 選択機構 + タグドット/絞り込みチップ + **作業モード=別スペースへ移動**(E-WORKSPACE-042.MoveImages) | **製造済** |
| **β-2** | **タグ編集モード**(E-UI-TAGASSIGN-029 再利用=現在のタグ/タグ追加/インライン値/数値・作業と排他) | **製造済** |
| **β-3** | **整理モード**(E-UI-SIMILARITY-035 + E-UI-MERGE-036 の整理トレイ=類似+マージ・UQ-W03 決定) | **製造済** |
| **β-4** | **⋯メニュー(削除/ゴミ箱 popup)**・削除モード=ゴミ箱へ移動・ゴミ箱 in-tab popup(復元/完全削除/空)・E-TRASH-038 / E-UI-REPAIR-039 再利用 | **製造済** |

## 2. β-1 製造(本コミット範囲)

`WorkTabViewModel` 拡張(ctor に `ITagRepository` 追加=タグドット/チップ算出のため):
- **タグ読込**: `_tagById`(全タグ)+ `_imageTags`(`GetAllImageTagsAsync` をグループ化)。`ImgTagIds` で画像→タグ id。
- **タグ絞り込みチップ**: 現スペース内の画像に付くタグから算出(クリア + タグ別件数)。`ClickChip` で `_tagFilter` 切替 → `Recompute` で Items 再構築。
- **タグドット**: 非作業モード時、グリッドセル左下にタグ色ドット(最大3)。
- **作業モード**(`ToggleWork`): 選択クリア・グリッド selectable 切替(membership 扱いで Recompute)。`HandleItemClick` → `ToggleSelect`(Shift 連続 / Ctrl トグル / 単一・表示順が選択母集合)。選択順バッジ(`SelectionOrderText`)。選択マーカーはその場更新(`RefreshSelectionMarkers`=Items 作り直さない・ECO-020 perf 規律)。
- **別スペースへ移動**(`ToggleMoveMenu` / `MoveSelectedTo`): 作業モード随伴ボタン(緑・選択数バッジ)+ 移動先スペース一覧 Popup。`WorkspaceService.MoveImages`(INV-W5 原子)→ membership 変化で再読込。`MoveTargetVM`(移動先候補)。

`WorkTabView.axaml`: ツールバー左に作業ボタン + 別スペースへ移動 Popup(MoveTrigger)・タグ絞り込みチップ行・グリッドセルに選択順バッジ+タグドット・グリッド/リスト行に `PointerPressed`(選択)。code-behind `OnItemPressed`(修飾キー読取)/`OnChipPressed`/`OnMenuClosed`。

**Core 意味論は不変**(MoveImages は ECO-020 で既証 CP-WORKSPACE-028)。DS は既存 `workActive`/`workAddBtn`/`workAddBadge`/`tagChip`/`thumbCheck` 等を再利用(新規 DS なし)。

## 3. 影響 BOM
- **E-UI-WORKSPACE-043**(拡張): β-1 で作業モード=別スペース移動・タグドット/絞り込みチップ・選択機構を配線。
- **E-WORKSPACE-042**(再利用): MoveImages(意味論不変)。
- ctor 拡張(`ITagRepository`)で構築サイト更新: MainWindowViewModel のみ(WorkTab は内部構築)。

## 4. 検証
- App build 0 警告(XAML コンパイル+compiled binding OK)/ Tests 447(+2 `CpUiG1WorkTabTests`)/ Oracle 74+2skip(S-01〜S-31 退行ゼロ)。
- `CpUiG1WorkTabTests`: チップ算出+絞り込みで Items 収束 / 作業モードで選択し別スペースへ移動(d1→d2 の membership 反映)。
- 実機 golden=maintainer ゲート(作業ボタン緑・選択順バッジ・別スペースへ移動の緑ボタン/件数バッジ/移動先メニュー・タグドット/絞り込みチップ・移動後の membership 反映)。

## 5. β-2 製造(タグ編集モード)

`WorkTabViewModel` 拡張(新 ctor 引数なし=`_tags` から `TagService` 構築):
- **タグ編集モード**(`ToggleEdit`): 作業と排他(`InAnyMode`・`ShowEditEntry`/`ShowWorkEntry` で **ECO-014§8 排他隠し**を作業タブへ拡張=モード中は他モード入口を隠す)。`inSelect = editMode || workMode` でグリッド選択を共有。
- **タグ編集パネル**(`BuildContextPanels`/`BuildAddGroups` を ImageTabViewModel から移植・`ImageRecord`+`_imageTags` で動作): 現在のタグ(共通タグ)/タグ追加(シンプル/テキスト/数値グループ・追加済バッジ・インライン候補値ピッカー・数値セル)。
- **コマンド**: `TabCurrent`/`TabAdd`・`ClickAddRow`(シンプル即付与/テキスト・数値は展開+設定ロード `EnsureSettingsAsync`)・`ApplyTextValue`・`ApplyRating`・`RemoveCurrentTag`。付与/削除は **TagService**(TagImagesAsync/UntagImagesAsync)経由 → `ReloadTagsAsync`(image_tags 再取得→Recompute)。Core 意味論不変。
- 末端 VM(CurrentTagVM/AddGroupVM/AddRowVM/ValueChipVM/NumCellVM)+ DS(editPanel/panelTab/tagAddRow/inlinePicker/numCell)を再利用(新規なし)。
- スコープ外: 連番別アクション(UQ-I02b・画像タブ固有)はモック作業タブに無いため非搭載。

`WorkTabView.axaml`: ツールバー左にタグ編集ボタン(排他表示)+ 右ペイン タグ編集パネル(EditMode 時)。code-behind `OnAddRowPressed`/`OnValueChipPressed`/`OnNumCellPressed`。

検証: App build 0 警告 / Tests 448(+1 タグ付与・削除)/ Oracle 74+2skip。

## 6. β-3 製造(整理モード=類似+マージ整理トレイ)

`WorkTabViewModel` 拡張(ctor に `SimilaritySearchService`+`MergeService` 追加=MainWindowVM 構築更新):
- **整理モード**(`ToggleOrganize`): タグ編集・作業と排他(3モード排他)。整理中はグリッドクリックが選択でなく**マージ先/整理対象の割当**(未設定→マージ先 / 設定後→整理対象トグル)。グリッドに**マージ先リング+「マージ先」ラベル / 整理対象チェック**(`IsMergeTarget`/`IsOrganizeTarget` その場マーカー)。
- **整理トレイ**(右ペイン・`BuildContextPanels` で構築): マージ先(残す1枚)/ 整理対象(まとめて削除・昇格/外す)/ 似た画像を探す(類似=しきい値スライダー / 条件=名前・拡張子)/ タグ統合チェック / マージを実行 / 完了状態(統合しました+別の整理を続ける)。
- **検索スコープ=現スペース内に限定**(集めて吟味してマージのシナリオ): 条件は `CriteriaMatcher.Match(_sourceImages,...)`(純粋関数・collection 非依存)/ 類似は `SimilaritySearchService.FindSimilarAsync` 結果を workspace の画像 id に絞る(類似は実 pHash 要=golden 確認)。
- **マージ**=`MergeService.MergeAsync`(E-MERGE-034・原子・タグ union INV-011・source=deleted・物理非破壊 INV-009)→ source は現スペースの normal 一覧から外れる(`ReloadWorkspacesAsync`)。**Core 意味論不変**。
- 末端 VM(OrganizeSlotVM/OrganizeResultVM)+ DS(traySection/organizeSlot/organizeMini/trayHint/segBtnText/matchBadge/resultCard/organizeDoneCard/primaryAction/cellTag/thumbCell.mergeTarget)を再利用(新規なし)。タグ統合 OFF の no-union と取り消し(Undo)は IMG-011(別 ECO)。

`WorkTabView.axaml`: ツールバー 整理ボタン(排他表示)+ 右ペイン整理トレイ + 中央検索結果ペイン(`ShowSearchResults` 時はグリッド/リストを譲る `ShowBrowseGrid`/`ShowBrowseList`)+ グリッドのマージ先リング/整理対象チェック。

検証: App build 0 警告 / Tests 449(+1 マージ先選択→条件検索→マージ実行→source deleted)/ Oracle 74+2skip。

## 7. β-4 製造(⋯メニュー=削除/ゴミ箱 popup)

`WorkTabViewModel` 拡張(ctor に `TrashService`+`IWindowService` 追加=MainWindowVM 構築更新):
- **⋯メニュー**(`ToggleMoreMenu`・browse 専用 `!InAnyMode`): **削除 / ゴミ箱**(deleted 件数バッジ)。
- **修復は非搭載**(設計判断): 修復(criteria/relink)は**コレクションスコープ**で、複数コレクションに跨りうる作業スペースに対応しないため作業タブ ⋯ から除外(削除/ゴミ箱は workspace に対応=採用)。
- **削除モード**(`EnterDelete`/`ExitDelete`・**4つ目の排他文脈モード**・⋯から入る・ツールバー入口なし): グリッド選択可(`inSelect` に削除追加)→「ゴミ箱へ移動」(赤・件数バッジ)で選択を `TrashService.DeleteToTrashAsync`(normal→deleted ソフト削除・物理非破壊 INV-009・復元可)。deleted は現スペース normal 一覧から外れる。3モード排他隠しを削除へ拡張(`InAnyMode` に削除追加)。
- **ゴミ箱 in-tab popup**(ECO-019 再利用・中央オーバーレイ 780×560): 現スペースの deleted 一覧(複数選択/すべて選択)・復元(`RestoreAsync`)・完全削除(`PermanentDeleteAsync`・確認)・空にする(確認)。**確認文言は INV-009 忠実**(「画像ファイルは削除されません(DB から除去)」)。
- **小 Core 追加(読み取りクエリのみ・新状態遷移なし)**: `IWorkspaceRepository.GetDeletedImagesAsync(workspaceId)` + `WorkspaceService.GetDeletedImagesAsync`(workspace_images × images status=deleted・file_name 昇順)=ゴミ箱 popup/件数バッジの workspace スコープ供給。完全削除は FK CASCADE で workspace_images も消滅。
- 末端 VM(TrashPopupItemVM)+ DS(delMoveBtn/delMoveBadge/trashCountBadge/trashPopup・*Header/Footer/trashCard/trashThumb/trashCheck/trashRestoreBtn/trashEmptyBtn)を再利用(新規 DS なし)。

`WorkTabView.axaml`: ツールバー ⋯メニュー(削除/ゴミ箱)+ 削除モードツールバー(削除を終了+ゴミ箱へ移動・赤)+ ルートを Panel ラップしてゴミ箱 popup 中央オーバーレイ。

検証: App build 0 警告 / Tests 451(+2: 削除→ゴミ箱→復元 / 完全削除で DB 行除去)/ Oracle 74+2skip。

## 7b. レビュー是正(2026-06-29・golden 前)

外部レビュー3件を精査=いずれも妥当として是正(回帰テスト +3):
- **①(P2)フィルタ変更で選択維持**: `ClickChip` が `_tagFilter` 変更時に `_selected` を保持 → 作業/削除モードで非表示画像を別スペース移動/ゴミ箱へ移動できた。**是正**: 絞り込みで非表示になった選択を `_selected` から落とす(交差)。注: golden 済み画像タブの `ClickChip` も同挙動(descriptive filter で選択維持)=作業タブ固有の退行ではない。作業タブは move/削除が破壊的なため作業タブのみ安全側へ。**画像タブは現状維持(maintainer 裁定 2026-06-29)**=golden 済み surface でパリティ範囲を広げないため未変更。画像タブも削除モードを持つため**長期は別 ECO で「フィルタ変更時の選択ポリシー(維持/交差/クリア)」を明示裁定**してから統一(backlog BL-002)。
- **②(P2)RefreshAsync が folderPath 未再読込**: 起動後追加コレクションの画像で `AbsolutePath` が null=サムネ/整理/ゴミ箱欠落。**是正**: `RefreshAsync` で `_folderPath` を再読込(InitializeAsync と同等)。
- **③(P3)⋯メニューが light-dismiss で閉じない**: `CloseMenusFromDismiss` が `MoreMenuOpen` を閉じず二度押し要。**是正**: `MoreMenuOpen=false` を追加。
- 検証: App build 0 警告 / Tests 454(+3)/ Oracle 74+2skip。

## 8. スコープ外(ECO-β 完了時点)
- 修復(コレクションスコープ・作業タブ非対応)。タグ統合 OFF/取り消し(IMG-011)。類似 find の実 pHash 検証は golden。詳細/ノート(REQ-043・別 ECO)。
