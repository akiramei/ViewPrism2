# Factory Run 4 報告 — ViewPrism2 (loop-v1-core) 収束再製造(BOM v1.2)

- 製造装置: factory-01
- 実施日: 2026-06-11
- 範囲: 仕様 §2.6 v1.2 への UI 収束 — M-UI-013 改修(シェル=タブ化・タグタブ 3 ペイン・画像タブ再構成)/ M-UI-016 新設(タグ付与パネル)/ Core・Infrastructure の最小拡張 / CP-TAGUI-013・CP-UI-G6(unit 部)の治具追加
- 隔離規律: 遵守(41-fixed-oracle.yaml・42-exploratory-probes.yaml・tests/ViewPrism2.Oracle/(中身)・原典 view-prism・BomDD リポジトリは未参照。bomdd/・docs/ 既存ファイルは未変更、本報告の新規作成のみ。git stash の前回中断作業は不使用 — 全て現作業ツリーから fresh 実装)
- cheat 分類: Run 1 自定義を継続 — C1=仕様/契約の欠落を補完 / C2=契約の曖昧さ・矛盾の解消 / C3=調達逸脱 / C4=治具・受入手段の判断 / C5=表面の独自判断 / C6=手戻り

## 1. 製造単位 → 成果物パス対応表

| 製造単位 | 成果物 |
|---|---|
| M-UI-013 シェル(v1.2) | `Views/MainWindow.axaml(+.cs)` — 上部タブナビゲーション「タグ」「画像」+右端「設定」ボタン。タブはボタン+アクセント下線、コンテンツは可視切替(状態保持)。旧メニューは廃止 |
| M-UI-013 タグタブ(3 ペイン) | `Views/TagsTabView.axaml(+.cs)`+`ViewModels/TagsTabViewModel.cs` — 左=ビュー管理(新規/一覧(name 昇順)/編集・削除アイコン/選択)、中央=階層構造エディタ、右=タグパレット。パレット→ツリーの D&D(Avalonia 12 DataTransfer API: `DataFormat.CreateStringApplicationFormat`+`DragDrop.DoDragDropAsync`。ノード上=子・空白=ルート)とボタン追加(ルート/子)の両経路 |
| M-UI-013 階層エディタ | `ViewModels/HierarchyEditorViewModel.cs`(+`EditNodeViewModel`)— **メモリ内編集+バッチ保存/キャンセル+確認+ダーティ追跡**(未保存変更がある間のみ活性)。ノード操作: 追加・展開/折畳(IsExpanded 双方向)・ホームタグ設定/解除(単一・★強調)・別名インライン編集(✎→TextBox、Enter/フォーカス喪失=確定・Esc=取消)・条件設定ダイアログ(textual=equals/pattern/values、numeric=equals/range)・削除(枝ごと)。保存は ViewService.SaveHierarchyAsync(単一 Tx・modified_at 1 回 — REQ-032) |
| M-UI-013 ビュー作成/編集 | `Views/ViewEditDialog.axaml(+.cs)`+`ViewModels/ViewEditDialogViewModel.cs` — 名前(必須)+説明(+お気に入り)。旧 ViewEditorWindow(基本情報+条件+階層の複合編集)は撤去 |
| M-UI-013 タグパレット/タグ作成 | `ViewModels/TagPaletteViewModel.cs`(検索=部分一致大文字小文字無視・追加・編集/削除・スウォッチ+種類チップ)/ `Views/TagEditorWindow.axaml(+.cs)` 改修 — カラー**プリセット(18 色)+hex 表示**、候補値の **D&D 並べ替え**(+既存 ↑↓)。旧 TagManagementWindow は撤去(パレットが代替) |
| M-UI-013 画像タブ | `MainWindow.axaml` 内 — 左=同期フォルダ(コレクション)一覧+スキャン実行(`FolderManagementViewModel` を左ペインへ埋め込み。追加・⟳スキャン・行内サマリ。詳細編集は「管理」→既存ダイアログ)+ビュー(お気に入り/最近)+NodeGraph ツリー+「全画像」固定入口 / ツールバー=グリッド/リスト・列数・ソート+**「タグ編集」モードトグル** / 右パネル=詳細⇔タグ付与の切替 |
| M-UI-016 タグ付与パネル | `ViewModels/TaggingPanelViewModel.cs`+`Views/TaggingPanelView.axaml` — 現在のタグ(複数選択時は共通タグ=積集合、各行に解除×)/ タグを追加(全タグ+検索)/ 適用=TagService の原子バッチのみ経由(個別ループなし、REQ-027)。textual=候補値ドロップダウン(predefined_values 順)+自由入力、simple=値なし、numeric=ダイアログ。選択 0 件プレースホルダ。適用後は選択を選択順のまま復元(`ImageBrowserViewModel.RestoreSelection`) |
| M-UI-016 数値ダイアログ | `ViewModels/NumericValueDialogViewModel.cs`+`Views/NumericValueDialog.axaml(+.cs)` — 固定値|選択順連番(開始値+選択順 i、増分 1)。min/max(両端含む)+step 刻みをダイアログ内検証し、不成立は適用 0 件。値は InvariantCulture(INV-007) |
| ノード条件ダイアログ | `ViewModels/NodeConditionDialogViewModel.cs`+`Views/NodeConditionDialog.axaml(+.cs)` — condition_value JSON(§2.4 スキーマ)の生成+検証(numeric は数値必須・range は from≦to・pattern は 1024 字上限+事前コンパイル検証 — K-REGEX) |
| ビューア起動経路(REQ-041 v1.2) | `ImageBrowserViewModel.SuppressOpenItem` — ダブルクリック=ビューア起動、**タグ編集モード中は無効**(シェルがモードと同期) |
| i18n | `Assets/i18n/{ja,en}.json` へ新規 22 キー(ja 正・en 併記、K-I18N 規約。既存 754 キーは無改変・全 776 キー)。新 UI の大半は既存原典キーを再利用(navigation.tags/images/settings、view.viewManagement、tag.searchTags、toolbar.tagEdit、modals.confirmDiscard.* 等) |
| M-HARNESS-015(Run 4 追加分) | `CpTagUi013Tests.cs`(8)/ `CpUiG6HierarchyEditorTests.cs`(9)/ `CpView012HierarchySaveTests.cs`(5)/ `CpUiG1SelectionTests.cs` へ 2 本追加 / `CpL1SmokeTests.cs` をシェル v1.2 対応+タブ・タグ編集モード検証へ拡張 / `CpDb006Tests.cs` に v0 初版 DDL フィクスチャを固定(マイグレーション追加に伴う) |
| DI | `App.axaml.cs` — FolderManagementViewModel / TagsTabViewModel / TaggingPanelViewModel を登録し MainWindowViewModel へ注入 |

## 2. Core/Infrastructure 拡張の変更点(Run 指示 8 の報告義務)

いずれも REQ-027(原子性)・REQ-032(保存時 1 回の modified_at)・INV-006/007 の意味論内の最小拡張:

1. **`View.Description`(Core/Models/Entities.cs)**: REQ-030 の description を v1.2 ダイアログ要求に合わせて実体化(null 可・非 required — 既存構築コードに影響なし)
2. **スキーマ(Infrastructure/Database/DatabaseSchema.cs)**: `views.description TEXT NULL` を **LatestDdl 末尾列**+マイグレーション `001-views-description`(ALTER TABLE ADD COLUMN)で追加。ALTER は末尾追加のため新規 DB とマイグレーション適用 DB の列順が一致(CP-DB-006 のスキーマ同値を維持)。Migrations が初めて非空になり、REQ-004 の増分適用が実動化(実機で Run 3 製 DB への適用を確認 — §3)
3. **`IViewRepository.ReplaceHierarchyAsync(viewId, nodes, homeNodeId, modifiedAt)`**(+ViewRepository 実装): 階層の一括置換。単一トランザクションで DELETE→INSERT(自己参照 FK のため親先行のトポロジカル順)→`home_tag_id`+`modified_at` を 1 回 UPDATE
4. **`ViewService.SaveHierarchyAsync(viewId, nodes, homeNodeId)`**: バッチ保存の core 意味論 — 検証(ビュー存在・id 重複・集合外親=ValidationError・循環=CircularReference(INV-004))→集合外ホームは null へフォールバック(REQ-037 と同方針)→置換+modified_at 1 回(REQ-032)
5. **`ViewService.GetAllAsync()`**: タグタブ「ビュー管理」一覧の閲覧供給(modified_at 不変)
6. **`ViewService.CreateAsync(..., description)`**: 末尾の省略可能引数(既存呼び出しに影響なし)
7. **`ITagRepository.TagImagesWithValuesAsync(tagId, assignments)`**(+TagRepository 実装)/ **`TagService.TagImagesWithValuesAsync`**: 画像ごとに異なる値の一括付与(連番適用、REQ-046)。全値を適用前検証(REQ-025 範囲)→単一トランザクション UPSERT、失敗時全ロールバック(REQ-026/027)
8. **App 層 VM の小改修**: `ImageBrowserViewModel` に `SuppressOpenItem`(REQ-041 v1.2)と `RestoreSelection`(適用後の選択順復元)、`FolderManagementViewModel` に `DataChanged` イベント(左ペイン埋め込みの再読込連鎖)
9. **撤去**: `TagManagementWindow(+VM)`・`ViewEditorWindow(+VM)`(v1.2 でタグパレット・ビュー編集ダイアログ・階層エディタに置換)。`IWindowService` は ShowTagManagementAsync/ShowViewEditorAsync を廃し、ShowViewEditDialogAsync/ShowNumericValueDialogAsync/ShowNodeConditionDialogAsync を追加

## 3. 受入実行ログ要約

- `dotnet build ViewPrism2.sln -c Release` → **成功(警告 0・エラー 0)**(TreatWarningsAsErrors=true。tests/ViewPrism2.Oracle もビルド対象に含まれコンパイル成功 — 中身は未読)
- `dotnet test tests/ViewPrism2.Tests -c Release` → **全 190 件成功(不合格 0・スキップ 0)**、実行時間 8.2s
- Run 1〜3 の既存 166 件は**全件退行なし**(+Run 4 追加 24 件)

| CP | depth | テスト数 | 結果 | 備考(test_vectors 被覆) |
|---|---|---|---|---|
| CP-TAGUI-013(新設) | unit | 8 | PASS | ①共通タグ算出(3 画像中 2 画像のみのタグは現在タグに出ない)②連番: 選択順 [C,A,B]・開始値 5 → C=5,A=6,B=7(id 順でない — FMEA-014)③固定値: 全選択画像に同値(ApplyCommand→ダイアログ→原子バッチ経路)④min=1,max=5 で 6 → ダイアログ内拒否・適用 0 件(+連番の途中超過拒否・境界 5 受理・step 刻み検証)⑤適用中 1 件失敗(FK 違反)→ 全ロールバック 0 件(REQ-027)⑥解除: × で当該タグのみ選択全件から解除(他タグ不変)⑦選択 0 件: プレースホルダ状態フラグ + 補助(textual 値必須・候補値順序・検索部分一致大文字小文字無視) |
| CP-UI-G6(unit 部・新設) | unit | 9 | PASS | ダーティ追跡(編集で活性化・クリーン時は Save/Cancel 不能)・編集はメモリ内に留まり保存前 DB 不変・保存で一括コミット+**modified_at は保存時刻で 1 回**(REQ-032、FakeClock で exact)・alias/条件/ホーム/子追加の永続化(position・parent 含む)・キャンセル=確認後破棄(いいえなら保持)・ノード削除は枝ごと・ホームタグ単一トグル・条件ダイアログ結果の反映・ビュー未選択/ノード 0 件の空状態フラグ・ConfirmDiscardIfDirtyAsync |
| CP-VIEW-012(Run 4 追加) | unit | 5 | PASS | description ラウンドトリップ(作成/更新/クリア)・一括置換保存(旧階層全置換+home_tag_id+modified_at 1 回)・循環=CircularReference/集合外親=ValidationError+拒否時は既存階層無傷(原子性)・集合外ホームの null フォールバック・存在しないビュー=NotFound |
| CP-UI-G1(Run 4 追加) | unit | +2 | PASS | タグ編集モード中(SuppressOpenItem)はダブルクリックでビューアを開かない(解除で復活)・RestoreSelection は選択順保持+見つからない id 読み飛ばし |
| CP-DB-006(改修) | L2 | 8 | PASS | v0 初版 DDL をフィクスチャ化し、`001-views-description` 適用後の新規 DB とのスキーマ同値(列順含む)を exact 検査。migrations 行数=定義数(1)も実動化 |
| CP-L1-SMOKE(拡張) | L1 | 1 | PASS | 既存正常系 1 本に加え: タブ切替(タグタブ遅延読込→ビュー一覧・パレットに実データ)・タグ編集モード(SuppressOpenItem 連動・選択→共通タグ表示)を in-process 検証 |
| 既存 13 CP(Run 1〜3) | unit/L2/L3 | 164 | PASS | 退行なし(CpL1SmokeTests/CpDb006Tests は v1.2 対応で改修、検査強度は維持・増強) |
| 合計 | — | **190** | **全件 PASS** | |

## 4. L1 スモーク記録(CP-L1-SMOKE)

実施日 2026-06-11、Release ビルドの `src/ViewPrism2.App/bin/Release/net10.0/ViewPrism2.App.exe` にて:

1. **起動・生存**: プロセス起動 → 8 秒後も生存、MainWindowTitle=`ViewPrism2` → PASS
2. **タブ切替(実 UI、UI Automation で実施)**: 「タグ」タブボタンを UIA Invoke → **3 ペイン見出し(ビュー管理/タグパレット/ビュー階層構造)と空状態「ビューを選択してください」が描画される**ことを UIA 要素列挙で確認 → 「画像」タブへ復帰 → プロセス生存継続 → PASS
3. **タグ編集モード(実 UI)**: ツールバー「タグ編集」を UIA Toggle → 右パネルがタグ付与パネルのプレースホルダ「タグ付けする画像を選択」へ切替 → 解除で詳細パネル「画像を選択してください」へ復帰 → PASS
4. **多重起動防止**: 2 つ目は即終了(exit 0)、1 つ目は生存継続 → PASS
5. **正常終了と永続化**: CloseMainWindow → 正常終了、settings.json が M-SET-010 スキーマどおり → PASS
6. **増分マイグレーションの実機適用(REQ-004)**: Run 3 までに生成済みの既存 `%APPDATA%/ViewPrism2/viewprism2.db` に対し、初回起動で `001-views-description` が適用され、migrations 行と `views` テーブルの live スキーマ(`..., modified_at TEXT NOT NULL, description TEXT NULL)`)を確認 → PASS
7. **フォルダ登録→スキャン→グリッド・タグ付与・階層保存の操作系**: フォルダピッカー・D&D・モーダルダイアログの UI 自動操作は困難なため、**実サービス+実 DB の in-process テストで代替**(CpL1SmokeTests / CpTagUi013Tests / CpUiG6HierarchyEditorTests — CHEAT-064)。画面描画の最終確認は golden(CP-UI-G1〜G7、承認者 maintainer)に委ねる

## 5. ずる報告(cheat-log 全件)

Run 3 からの通し番号(CHEAT-053 以降)。

### CHEAT-053 [C2] views.description のスキーマ契約欠落
- 手法が与えなかったもの: REQ-030 と v1.2 §2.6(ダイアログ=名前+説明)は description を要求するが、仕様 §2.0 のエンティティ記述と M-DB-007 のスキーマ契約に views の description 列が無い(v1.2 改訂で M-BOM 側が未追随)
- 代替した判断(何をどう埋めたか): `views.description TEXT NULL` を LatestDdl(末尾列)+マイグレーション `001-views-description` で追加し、エンティティ・リポジトリ・サービスへ貫通させた
- 重大度: friction

### CHEAT-054 [C2] numeric step の「ダイアログ内検証」の意味論未規定
- 手法が与えなかったもの: v1.2 は「min/max/step をダイアログ内で検証」と規定するが、REQ-025 は min/max のみで step の検証規則が無い
- 代替した判断(何をどう埋めたか): (value − min)/step が整数(許容誤差 1e-9、min 未設定時は 0 基準)であることを検証。連番の増分は仕様の「開始値+選択順 i」どおり固定 1(step とは独立)
- 重大度: minor

### CHEAT-055 [C2] 「右端に『設定』」の形態が未規定
- 手法が与えなかったもの: v1.2 シェルの「設定」がタブかボタンか、設定画面の形態
- 代替した判断(何をどう埋めたか): タブ条の右端ボタン+既存の設定モーダル(REQ-051 の言語切替)を流用。タブ化すると未保存編集との相互作用が増えるため最小変更を選択
- 重大度: minor

### CHEAT-056 [C2] view_conditions 編集 UI が v1.2 §2.6 から消えた
- 手法が与えなかったもの: 旧 Run 3 の ViewEditorWindow は条件(REQ-031 の入力)編集を持っていたが、v1.2 のタグタブ仕様(ビュー管理=名前+説明、中央=階層のみ)に条件編集の記述が無い
- 代替した判断(何をどう埋めたか): v1.2 への収束を優先し ViewEditorWindow を撤去、**条件編集 UI は未提供**とした(評価器・条件 CRUD の core/API は維持。既存 DB の条件はビュー評価に引き続き効く)
- 重大度: friction(意図どおりか設計者確認を要望 — §7 申し送り 1)

### CHEAT-057 [C5] プリセットカラーの色値(必須報告次元)
- 手法が与えなかったもの: v1.2「カラー(プリセット+hex 表示)」のプリセット定義が K-DESIGN に無い
- 代替した判断(何をどう埋めたか): 原典 i18n の colorPicker.colorCategories 18 カテゴリ名(red〜blueGrey)に対応する Material Design 500 の 18 色(#F44336〜#607D8B)を 20px 円形スウォッチで提供。選択は hex 欄へ反映(自由入力併用・REQ-023 検証は core)
- 重大度: minor

### CHEAT-058 [C2] タグタブの細部規則の確定
- 手法が与えなかったもの: ビュー一覧の並び・初期表示タブ・タブの遅延読込・選択中ビューの強調
- 代替した判断(何をどう埋めたか): ビュー一覧=name 昇順(OrdinalIgnoreCase・同値 id 昇順、REQ-029/038 の整列慣行を流用)/ 初期タブ=「画像」/ タグタブは初回表示時に遅延読込 / 選択中ビュー・パレット選択タグはアクセント 12% 背景(既存 navItem 流用)
- 重大度: minor

### CHEAT-059 [C2] 同期フォルダ詳細編集の置き場
- 手法が与えなかったもの: v1.2 画像タブ左は「同期フォルダ一覧+スキャン実行」のみで、REQ-010 の編集(exclude_patterns・サブフォルダ・有効化)と再リンク入口の置き場が無い
- 代替した判断(何をどう埋めたか): 左ペインは一覧+⟳スキャン+追加+行内サマリに絞り、詳細編集・再リンクは既存「同期フォルダ管理」ダイアログを「管理」ボタンで温存
- 重大度: minor

### CHEAT-060 [C2] 共通タグの値表示(複数選択・値不一致)
- 手法が与えなかったもの: REQ-046 は「共通タグ」とのみ規定。選択画像間で値が異なる場合の表示
- 代替した判断(何をどう埋めたか): 全選択画像で同値のときのみ値を表示、不一致は値非表示(タグ名のみ)。解除は値に関わらずタグ単位で全件解除
- 重大度: minor

### CHEAT-061 [C5] K-DESIGN に無い寸法・表現(必須報告次元・全列挙)
- 手法が与えなかったもの: v1.2 新規 UI の寸法群
- 代替した判断(何をどう埋めたか): タブボタン padding 16×8・選択タブ=アクセント 2px 下線+主文字色 / タグタブ ペイン幅 左 240・右 300 / 種類チップ=境界線色 1px・角丸 8・11px / プリセットスウォッチ 20px 円形 / ノード条件サマリ 11px 弱色 / ダイアログ幅 380〜420。余白は 4px グリッドから選択
- 重大度: minor

### CHEAT-062 [C2] D&D の操作・視覚仕様未規定
- 手法が与えなかったもの: v1.2「タグパレットから D&D」「候補値の D&D 並べ替え」の操作詳細(開始条件・ドロップ位置規則・視覚フィードバック)
- 代替した判断(何をどう埋めたか): パレット行は左ボタン押下で選択+ドラッグ開始(Copy)。ドロップ先=ノード上なら子・空白ならルート末尾。候補値は Move で対象行の位置へ挿入(行外ドロップは末尾)。視覚は OS 標準のドラッグカーソルのみ(独自ゴースト・挿入線は未実装)。ボタン経路(ルートに追加/子として追加・↑↓)を常時併設
- 重大度: minor

### CHEAT-063 [C2] i18n 新規キー 22 件の追加・命名(必須報告次元・全列挙)
- 手法が与えなかったもの: タグ付与パネル・数値ダイアログ・階層エディタ補助の文言キー
- 代替した判断(何をどう埋めたか): K-I18N の `<画面>.<要素>` 規約で ja 正・en 併記(既存 754 キーは無改変、計 776):
  `tagging.currentTags`・`tagging.apply`・`tagging.noCommonTags`・`tagging.valueRequired`・`tagging.numericDialog.title`・`tagging.numericDialog.fixed`・`tagging.numericDialog.sequential`・`tagging.numericDialog.startValue`・`tagging.numericDialog.outOfRange`・`tagging.numericDialog.stepMismatch` / `hierarchy.setHome`・`hierarchy.clearHome`・`hierarchy.editAlias`・`hierarchy.editCondition`・`hierarchy.conditionDialog.title`・`hierarchy.conditionType.none`・`hierarchy.conditionType.equals`・`hierarchy.conditionType.range`・`hierarchy.conditionType.pattern`・`hierarchy.conditionType.values`・`hierarchy.conditionValuesHint`・`hierarchy.dropToAdd`。
  新 UI の主要文言は既存原典キーを最大限再利用(navigation.tags/images/settings・view.viewManagement/newView/noViews/selectViewPlease/noTagsAdded/hierarchyStructure・tag.searchTags/addTag/noTags/colorPicker.presetColors・toolbar.tagEdit・modals.confirmDiscard.*・image.feature.selectImagesForTagging・common.selectedCount 等)
- 重大度: minor

### CHEAT-064 [C4] L1 スモークの一部操作を in-process 代替(必須報告)
- 手法が与えなかったもの: フォルダピッカー・D&D・モーダルダイアログ操作の UI 自動化手段(UIA 治具は調達外のため OS 標準 UIA を最小限使用)
- 代替した判断(何をどう埋めたか): 実 exe では起動/生存/多重起動/正常終了+永続化+増分マイグレーションに加え、**タブ切替と右パネル切替を OS 標準 UI Automation の Invoke/Toggle で実施**(§4 の 2・3)。スキャン→グリッド・付与・階層保存は実サービス+実 DB の in-process テストで代替し、最終的な画面確認は golden(maintainer)に委ねる
- 重大度: minor

### CHEAT-065 [C2] ノード並べ替え・親付け替え UI の不在
- 手法が与えなかったもの: v1.2 のノード操作列挙(展開/折畳・ホーム・別名・条件・削除・D&D/ボタン追加)に、同一親内の position 変更・既存ノードの親付け替えが含まれない
- 代替した判断(何をどう埋めたか): 列挙どおり未提供(追加は常に兄弟末尾)。core の MoveNodeAsync(循環拒否)は維持
- 重大度: minor

### CHEAT-066 [C2] タブ間のデータ同期タイミング未規定
- 手法が与えなかったもの: タグタブでの永続変更(タグ・ビュー・階層保存)を画像タブの表示へ反映するタイミング
- 代替した判断(何をどう埋めたか): タグタブの変更で stale フラグを立て、「画像」タブへ復帰した時点で一括再読込(選択ビュー/ノードは可能なら復元)。タグ付与適用後は基礎データ再読込+選択を選択順のまま復元
- 重大度: minor

**CHEAT 集計: 14 件(blocker 0 / friction 2 / minor 12)**

### 導出メモ(cheat ではなく BOM・Run 指示からの導出と判断したもの)
- Core/Infrastructure 拡張(§2 の 1〜8)は Run 指示 8「仕様の意味論を守る範囲で必要最小限」の明示許可範囲として実施し、本報告で全列挙
- 連番の増分=1 は REQ-046/CP-TAGUI-013 ベクタ「開始値 5 → 5,6,7」から導出(step 非依存)
- バッチ保存時の home_tag_id 更新は「ホームタグ設定/解除」がバッチ編集対象であることから SaveHierarchyAsync に同梱(modified_at と同一トランザクション・同一タイムスタンプ)
- 階層エディタで参照切れタグのノードは表示・削除可能のまま保持(INV-008。保存時は現状維持で再 INSERT — タグ削除済みなら DB 側 FK が拒否し原子的に失敗する。実際にはタグ削除時に CASCADE で当該ノードが消えるため、タグ変更イベントで非ダーティ時はエディタを再読込して整合させる)
- ダーティ中のビュー切替・タグ変更時のエディタ保護(再読込しない)は「未保存変更の喪失防止」優先の導出
- D&D は Avalonia 12 の DataTransfer API(`DataFormat`/`DataTransferItem`/`DoDragDropAsync`)— K-AVALONIA に D&D 規定が無いため公式 API の標準用法のみ使用(パッケージ追加なし)
- tests/ViewPrism2.Oracle は自己受入の実行対象に含めず(隔離規律: 中身を読まない。実行時の失敗出力が oracle 内容を露出するため)。ビルド成功のみ確認

## 6. blocked 単位

なし。

## 7. 申し送り(設計者向け)

1. **view_conditions 編集 UI の扱い(CHEAT-056)**: v1.2 §2.6 に条件編集の記述が無いため UI を撤去した。REQ-031 の入力(条件 CRUD)は core/API に残っているので、意図的削除(NodeGraph パス条件で代替)か V2 で復活かの裁定を要望
2. **views.description のスキーマ追加(CHEAT-053)**: 仕様 §2.0・M-DB-007 の契約表へ `views.description TEXT NULL` の反映を推奨。マイグレーション `001-views-description` が初の増分適用例(固定オラクル S-05 が「Migrations=空」を前提にしている場合は設計者側での確認を推奨)
3. **M-BOM 改訂候補**: SaveHierarchyAsync/ReplaceHierarchyAsync(バッチ保存)・TagImagesWithValuesAsync(値付き一括付与)の interface_contract 追記、IWindowService の新ダイアログ 3 種
4. K-DESIGN への追補候補: タブ表現・プリセットカラー 18 色・チップ/ペイン幅(CHEAT-057/061)— golden 往復の削減
5. step 検証の意味論(CHEAT-054)と共通タグの値表示規則(CHEAT-060)の仕様化を推奨
6. golden(CP-UI-G1〜G7)は maintainer の目視承認待ち。確認手順例: 起動→画像タブ(フォルダ追加→⟳スキャン→グリッド)→タグタブ(タグ追加(型別ダイアログ・プリセットカラー・候補値 D&D)→新規ビュー(名前+説明)→ビュー選択→パレットからツリーへ D&D/ボタン追加→別名✎・条件⚙・ホーム★→保存/キャンセル)→画像タブ(ビュー選択→NodeGraph 絞り込み→「タグ編集」トグル→複数選択→現在タグ/追加/適用(numeric 固定値・連番)→解除×→ダブルクリック無効確認→トグル解除→ダブルクリックでビューア)→設定で ja/en 切替
