# Factory Run 3 報告 — ViewPrism2 (loop-v1-core) 最終 Run: 表面部品

- 製造装置: factory-01
- 実施日: 2026-06-11
- 範囲: M-THUMB-008 / M-I18N-011(資産統合)/ M-UI-013 / M-UI-014 / M-HARNESS-015(CP-THUMB-007 / CP-UI-G1/G2/G4 unit 部 / CP-I18N-010 追加分 / CP-L1-SMOKE)
- 隔離規律: 遵守(41-fixed-oracle.yaml・42-exploratory-probes.yaml・原典 view-prism・BomDD リポジトリは未参照。bomdd/・docs/ 既存ファイルは未変更。本報告の新規作成のみ。bomdd/assets/i18n/ は指示どおり読取・コピーのみ)
- cheat 分類: Run 1 自定義を継続 — C1=仕様/契約の欠落を補完 / C2=契約の曖昧さ・矛盾の解消 / C3=調達逸脱 / C4=治具・受入手段の判断 / C5=表面の独自判断 / C6=手戻り

## 1. 製造単位 → 成果物パス対応表

| 製造単位 | 成果物 |
|---|---|
| M-THUMB-008 | `src/ViewPrism2.Infrastructure/Imaging/ThumbnailService.cs`(K-SKIA 定型: 長辺 256・inside・拡大なし・Round half away from zero・最小 1px・PNG→PNG/他→JPEG q80・キャッシュキー MD5(小文字絶対パス)・失敗時 null+キャッシュ記録なし・破損キャッシュ削除再生成・GetDimensionsAsync=SKCodec ヘッダ読み・全 SK オブジェクト using 破棄・INV-009 読み取り専用オープン)。Infrastructure へ SkiaSharp 3.119.4 追加(調達表内) |
| M-I18N-011(資産統合) | `src/ViewPrism2.App/Assets/i18n/ja.json・en.json`(設計者供与 706 キー+V1 新規 48 キー=754 キー、UTF-8 BOM なし・フラット形式・ja/en 同一キー集合)/ `src/ViewPrism2.Infrastructure/I18n/I18nResourceLoader.cs`(資産→LocalizationService 注入形への変換。欠落・破損は空辞書で REQ-050 フォールバックに委ねる)。App 起動時に settings.locale で初期化 |
| M-UI-013(ViewModel) | `src/ViewPrism2.App/ViewModels/` — `MainWindowViewModel.cs`(シェル統括: ビュー一覧・NodeGraph 構築→ResolveHome→選択復元・パス→条件→評価→整列の Core 呼び出し・警告のステータスバー通知)/ `ImageBrowserViewModel.cs`(選択=クリック単一・Ctrl トグル・選択順バッジ 1 起点昇順・renumber、空状態判定、列数 3/4/5/6 既定 4、行リスト化、ソート、リスト列幅 star 按分)/ `DetailPanelViewModel.cs`(REQ-043 全項目+ノート保存)/ `FolderManagementViewModel.cs`(登録・重複拒否・削除確認・スキャン進捗+サマリ・除外パターン)/ `RelinkViewModel.cs`(REQ-017 候補列挙+確認+確定)/ `TagManagementViewModel.cs`+`TagEditorViewModel.cs`(REQ-021〜025・定義済み値並べ替え)/ `SettingsViewModel.cs`(言語即時切替+永続化)/ `ViewEditorViewModel.cs`(ビュー CRUD・階層ノード・条件編集)/ `ViewListItemViewModel.cs`・`GraphNodeViewModel.cs`・`LocalizationProxy.cs`・`DisplayColumnParser.cs`・`ErrorMessages.cs`(error.<code> キー解決)・`ImageEntry.cs`・`LocaleFormats.cs` |
| M-UI-013(View) | `src/ViewPrism2.App/Views/` — `MainWindow.axaml(+.cs)`(左=全画像/お気に入り/最近+NodeGraph ツリー、中央=ツールバー+グリッド⇔リスト、右=詳細、メニュー、ステータスバー。コードビハインドはポインタ→VM・TreeView 選択同期・幅供給のみ)/ `FolderManagementWindow`・`RelinkWindow`・`TagManagementWindow`・`TagEditorWindow`・`SettingsWindow`・`ViewEditorWindow`・`ConfirmDialog` / `Services/IWindowService.cs`+`WindowService.cs`(K-MVVM ダイアログ抽象)/ `Controls/ThumbnailImage.cs`(遅延サムネイル: Task.Run デコード+UI スレッド代入)/ `App.axaml`(K-DESIGN トークン 8 色)/ `App.axaml.cs`(DI 合成・Serilog 日次 7 世代・ウィンドウ状態の復元/保存 REQ-052)/ `Program.cs`(named Mutex `Global\ViewPrism2` 多重起動防止) |
| M-UI-014 | `src/ViewPrism2.App/ViewModels/ViewerViewModel.cs`(Next/Prev 端停止・CurrentPositionText="n / total"・空一覧安全・Close イベント)/ `Views/ViewerWindow.axaml(+.cs)`(KeyBindings: Right/PageDown/Left/PageUp/Escape。Image Stretch=Uniform+StretchDirection=DownOnly=縮小のみ。ロードは ImageMemoryCache 経由・Task.Run デコード) |
| M-HARNESS-015(Run 3 追加分) | `tests/ViewPrism2.Tests/` — `ImageFixtures.cs`(SkiaSharp 生成 jpg/png/webp+手書き最小 gif/bmp+壊れた jpg)・`CpThumb007Tests.cs`・`CpUiG1SelectionTests.cs`・`CpUiG2ColumnsTests.cs`・`CpUiG4ViewerTests.cs`・`CpI18n010AssetTests.cs`・`CpL1SmokeTests.cs`。全テストに `[Trait("cp", "CP-xxx")]`。Tests から App をプロジェクト参照(VM は Avalonia 非依存のため headless 実行可) |
| 調達 | Infrastructure: SkiaSharp 3.119.4 / App: CommunityToolkit.Mvvm 8.4.2・Microsoft.Extensions.DependencyInjection 10.0.*・Microsoft.Extensions.Logging 10.0.*・Serilog.Extensions.Logging 9.*・Serilog.Sinks.File 7.*(全て調達表記載内)。Avalonia.Diagnostics 12.0.4 は NuGet 未発行のまま(Run 1 CHEAT-002 継続、参照せず) |
| リポジトリ拡張(導出) | `IImageRepository.GetAllNormalAsync()`・`ITagRepository.GetAllImageTagsAsync()` を追加(表示系の INV-010 供給元と OC-1 入力構築用。Run 2 の GetDistinctNormalTagValuesAsync と同型の導出) |

## 2. 受入実行ログ要約

- `dotnet build ViewPrism2.sln -c Release` → **成功(警告 0・エラー 0)**(TreatWarningsAsErrors=true)
- `dotnet test tests/ViewPrism2.Tests -c Release` → **全 166 件成功(不合格 0・スキップ 0)**、実行時間 1.8s
- Run 1+2 の 132 件は全件退行なし

| CP | depth | テスト数 | 結果 | 備考(test_vectors 被覆) |
|---|---|---|---|---|
| CP-THUMB-007 | L2 | 9 | PASS | 1920x1080 jpg→256x144 jpg(±1px)・100x50 png→100x50 png(拡大なし・PNG 維持、FMEA-012)・gif/bmp/webp→jpg・丸め half away from zero(511x100→256x50)+最小 1px(2000x1→256x1)・キャッシュヒットで mtime 不変・キャッシュキー MD5 小文字絶対パス(大文字小文字同一視)・壊れた jpg→null+キャッシュ記録なし+再試行(FMEA-012)・0 バイト破損キャッシュ→削除+再生成・GetDimensionsAsync(寸法/破損 null) |
| CP-UI-G1(unit 部) | unit | 9 | PASS | クリック単一選択(置換)・Ctrl トグル+選択順バッジ 1 起点昇順・中間解除で採番し直し・ダブルクリック=表示要求(選択不変)・空状態判定(0 件・差し替え)・選択クリア・行リスト分割(4/5/3 列)・整列(OrdinalIgnoreCase asc/desc)・セル辺算出 |
| CP-UI-G2(unit 部) | unit | 5 | PASS | 既定 3 列(name 2*/size/modified_date)・JSON 定義どおりの列構成(basic+tag・label・width)・削除済みタグ列の無視+残り star 按分(AUDIT-102)・タグ列ラベル省略=タグ名・不正 JSON/未知キーは既定列 |
| CP-UI-G4(unit 部) | unit | 7 | PASS | 初期位置「1 / 3」・Next 末尾停止・Prev 先頭停止・空一覧「0 / 0」+ナビ安全(FMEA-002)・開始位置クランプ・CurrentImagePath 追随・Close 発火 |
| CP-I18N-010(Run 3 追加) | unit | 3 | PASS | 資産読込+ja/en 同一キー集合(754 キー)・V1 新規キー 12 種の両言語存在+ja/en 解決+プレースホルダ補間・資産ディレクトリ欠落でも例外なし(キー文字列フォールバック) |
| CP-L1-SMOKE(in-process 部) | L1 | 1 | PASS | 実画像 3 枚 → フォルダ登録 → ScanService(added=3)→ MainWindowViewModel 初期化 → グリッド 3 件・1 行 → サムネイル 3 件生成 → 選択 → 詳細パネル表示 → タグ付け+ビュー+階層 → NodeGraph 一体型「色: 赤」→ ノード選択で 1 件に絞り込み → ルートで 3 件 |
| Run 1+2 既存 12 CP | unit/L2/L3 | 132 | PASS | 退行なし |
| 合計 | — | **166** | **全件 PASS** | |

## 3. L1 スモーク記録(CP-L1-SMOKE)

実施日 2026-06-11、Release ビルドの `src/ViewPrism2.App/bin/Release/net10.0/ViewPrism2.App.exe` にて:

1. **起動・生存**: プロセス起動 → 8 秒後も生存、MainWindowTitle=`ViewPrism2`(ウィンドウ生成・クラッシュなし)→ PASS
2. **多重起動防止**: 1 つ目起動中に 2 つ目を起動 → 2 つ目は即終了(exit code 0)、1 つ目は生存継続(named Mutex `Global\ViewPrism2`)→ PASS
3. **正常終了と永続化**: CloseMainWindow による終了 → 正常終了、`%APPDATA%/ViewPrism2/settings.json` が M-SET-010 スキーマどおり生成(locale=ja / 1200x800 / gridColumns=4 / lastViewId=null)→ PASS
4. **DB 初期化**: 初回起動で `%APPDATA%/ViewPrism2/viewprism2.db` が WAL モードで生成 → PASS
5. **フォルダ登録→スキャン→グリッド表示**: UI 自動操作(フォルダピッカー等)が困難なため、**実サービス+シェル ViewModel の in-process テストで代替**(§2 の CP-L1-SMOKE 行。登録→スキャン→グリッド行生成→サムネイル生成→NodeGraph 絞り込みまで実 DB・実ファイルで通過)。**代替実施である旨を報告する**(CHEAT-050)。画面描画の最終確認は golden(CP-UI-G1〜G5、承認者 maintainer)に委ねる

## 4. ずる報告(cheat-log 全件)

Run 2 からの通し番号(CHEAT-035 以降)。

### CHEAT-035 [C1] DB ファイルのパス・ファイル名が未規定
- 手法が与えなかったもの: settings.json・thumbnails・logs は %APPDATA%/ViewPrism2 配下で規定済みだが、SQLite DB の配置・名前の規定が無い
- 代替した判断(何をどう埋めたか): `%APPDATA%/ViewPrism2/viewprism2.db` とした
- 重大度: minor

### CHEAT-036 [C2] 「読み取り不能なキャッシュファイル」の検出方法が未規定
- 手法が与えなかったもの: REQ-040 は「削除して再生成」のみ規定。何をもって読み取り不能と判定するか(存在チェック/サイズ/デコード)が無い
- 代替した判断(何をどう埋めたか): キャッシュヒット時に SKCodec ヘッダデコード(フルデコードなし)で検証し、失敗(0 バイト含む)で削除+再生成。ヒットごとに僅かな I/O コストが乗る
- 重大度: minor

### CHEAT-037 [C4] gif/bmp フィクスチャは SkiaSharp で生成できない
- 手法が与えなかったもの: 指示は「画像フィクスチャは SkiaSharp でテスト内生成」だが、SkiaSharp は GIF/BMP のエンコードをサポートしない(PNG/JPEG/WebP のみ)
- 代替した判断(何をどう埋めたか): gif=最小 GIF89a(1x1)・bmp=24bit 無圧縮ヘッダをテストコードでバイト列生成(いずれも SKBitmap.Decode 可能なことをテスト自身が検証)。jpg/png/webp は SkiaSharp 生成
- 重大度: minor

### CHEAT-038 [C1] 「全画像」固定入口の提供を工場判断で確定
- 手法が与えなかったもの: 仕様 §6 は「『全画像』相当を UI 上の固定入口として提供するか E-BOM で判断」としたが、E-BOM に判断の記載が無い
- 代替した判断(何をどう埋めたか): 左ペイン先頭に固定項目「全画像」(DB 上のビューではない)を提供。status=normal かつ is_active フォルダ配下の全画像を無条件表示。これが無いとビュー・タグ未設定の初回利用でグリッドに到達できず、CP-L1-SMOKE の「スキャン→グリッド表示」も成立しない
- 重大度: friction

### CHEAT-039 [C2] REQ-041「ダブルクリック=詳細を開く」と常時表示詳細パネルの整合+ビューア起動経路の発明
- 手法が与えなかったもの: M-UI-013 のシェルは右ペインに詳細パネルを常時表示するため「詳細を開く」操作が冗長。一方 REQ-044 ビューアの起動経路はどこにも規定が無い
- 代替した判断(何をどう埋めたか): 単一クリック選択で詳細パネルが即時更新(=詳細は常時開いている)とし、ダブルクリックをビューアウィンドウ(REQ-044)の起動に割り当てた
- 重大度: friction

### CHEAT-040 [C2] ビュー条件(REQ-031)とノードパス条件(REQ-036)の合成規則が未規定
- 手法が与えなかったもの: ビューは「条件・ソート・列・階層を束ねる」とされるが、view_conditions と選択ノードのパス条件を同時にどう適用するかの規定が無い
- 代替した判断(何をどう埋めたか): 両者を連結して単一の AND 条件列として評価器(OC-1)に渡す
- 重大度: minor

### CHEAT-041 [C2] ツールバーのソート変更をビュー定義へ永続化するかが未規定
- 手法が与えなかったもの: ビューに sort_field/direction があり、ツールバーに「ソート」がある。切替時にビューを更新するかの規定が無い
- 代替した判断(何をどう埋めたか): セッション表示状態とし永続化しない(ビュー選択時に定義値で初期化)。永続化すると閲覧操作で modified_at が更新され「最近」一覧が常時並べ替わるため(REQ-032「閲覧では更新しない」の趣旨を優先)
- 重大度: minor

### CHEAT-042 [C5] 通知形態の独自判断(必須報告次元)
- 手法が与えなかったもの: REQ-031 の「非モーダル通知」、スキャン結果サマリ・保存完了等の表示形態の規定
- 代替した判断(何をどう埋めたか): メインウィンドウ下部の常設ステータスバー(高さ 28px・1 行・テキスト弱色)に最新メッセージを表示。スキャン結果はフォルダ管理ウィンドウの行内テキスト。トースト・ダイアログは新設しない
- 重大度: minor

### CHEAT-043 [C5] K-DESIGN に無い寸法・配色(必須報告次元・全列挙)
- 手法が与えなかったもの: パネルレイアウトの寸法群
- 代替した判断(何をどう埋めたか): 左ペイン幅 240px / 右詳細パネル幅 280px / 選択順バッジ直径 20px(白文字 12px)/ リスト行・ビュー一覧の選択背景=アクセント色 12%(#1F3B82F6)/ ステータスバー高 28px / ビューアウィンドウ既定 1000×700・最小 400×300 / 詳細パネルのプレビュー領域高 160px / セル角丸 2px・ナビ項目角丸 4px。余白は 4px グリッド(4/8/12/16)から選択
- 重大度: minor

### CHEAT-044 [C2] M-UI-014 nav 契約の入力型と表示要件の不整合
- 手法が与えなかったもの: 契約は `ViewerViewModel(IReadOnlyList<ImageRecord>, int)` だが、表示には絶対パス(同期フォルダ path+relative_path)が必須で ImageRecord 単体では解決できない。タイトルの表示形式も「現在位置/総数を表示」のみ
- 代替した判断(何をどう埋めたか): `ImageEntry(Record, AbsolutePath, Tags)` のリストを受ける形に変更(CurrentPositionText="n / total" は契約どおり)。タイトルは「{ファイル名} — {n / total}」
- 重大度: minor

### CHEAT-045 [C5] リスト表示のタグ列で simple タグ(value=NULL)のセル表示が未規定
- 手法が与えなかったもの: REQ-042 は列定義のみでセル内容の表示形式が無い
- 代替した判断(何をどう埋めたか): 付与あり=「✓」・付与なし=空欄。textual/numeric は値文字列
- 重大度: minor

### CHEAT-046 [C2] 日時の「ロケール書式」表示のタイムゾーンと書式が未規定
- 手法が与えなかったもの: REQ-043 は「ロケール書式」のみ。UTC のまま出すかローカル時刻か、書式の粒度
- 代替した判断(何をどう埋めたか): ローカル時刻へ変換し .NET "g" 書式(短い日付+時刻)。culture は ja→ja-JP / en→en-US(格納は INV-002 のまま不変)
- 重大度: minor

### CHEAT-047 [C2] 詳細パネルの表示対象(複数選択時)と設定保存タイミングが未規定
- 手法が与えなかったもの: REQ-043 は単一画像の項目列挙のみ。REQ-052 は保存項目のみ
- 代替した判断(何をどう埋めたか): 詳細パネルは「最後に選択した画像」を表示(選択 0 件はプレースホルダ)。settings.json はウィンドウ Closing で一括保存(言語のみ切替時に即時保存 — REQ-051 の永続化要求のため)
- 重大度: minor

### CHEAT-048 [C5] タグ色選択 UI の簡略化と、表示列(display_columns)・ホームタグ設定 UI の未提供
- 手法が与えなかったもの: E-UI-TAGS-026「色選択」・E-UI-NODEGRAPH-025「列設定」の具体仕様(K-DESIGN にプリセットパレット定義なし)
- 代替した判断(何をどう埋めたか): 色選択は #RRGGBB テキスト入力+リアルタイムスウォッチのみ(原典系のプリセットパレットは再現せず)。display_columns と home_tag_id の編集 UI は V1 では未提供(リスト表示自体は DB 値・既定 3 列で REQ-042 どおり動作。ホームタグ解決 REQ-037 も実装済みで、値は DB 直接設定で機能する)
- 重大度: friction

### CHEAT-049 [C2] i18n 新規キー 48 件の追加・命名(必須報告次元・全列挙)
- 手法が与えなかったもの: V1 画面(空状態・再リンク・スキャン結果・詳細・エラー語彙)の文言キー
- 代替した判断(何をどう埋めたか): K-I18N の `<画面>.<要素>` 規約で ja 正・en 併記の 48 キーを新設(両言語に同時追加、原典変換 706 キーは無改変):
  `menu.manage` / `view.allImages`・`view.recent` / `toolbar.columns` / `detail.title`・`detail.selectImagePrompt`・`detail.resolution`・`detail.notes` / `folder.management`・`folder.add`・`folder.selectFolder`・`folder.isActive`・`folder.includeSubfolders`・`folder.excludePatterns`・`folder.lastScan`・`folder.neverScanned`・`folder.scanning`・`folder.scanSummary`・`folder.deleteConfirm`・`folder.empty` / `relink.title`・`relink.missingImages`・`relink.noMissingImages`・`relink.candidates`・`relink.noCandidates`・`relink.confirmMessage`・`relink.commit`・`relink.success`・`relink.failed` / `tag.usageCount`・`tag.editor.unit`・`tag.editor.step` / `viewEditor.addRootNode`・`viewEditor.addChildNode`・`viewEditor.alias`・`viewEditor.selectTag`・`viewEditor.operator`・`viewEditor.value`・`viewEditor.value2` / `nodeGraph.empty` / `error.duplicateTagName`・`error.duplicateFolderPath`・`error.validationError`・`error.notFound`・`error.circularReference`・`error.scanInProgress`・`error.ioError`・`error.invalidRegex`(error.* は M-BOM silence_sweep「error.<code>」の具現)。
  スキャン結果は原典の分割キー群(image.feature.* の件数別)を組み合わせず単一キー `folder.scanSummary` とした(REQ-015 サマリ 5 項目の一括表示)
- 重大度: minor

### CHEAT-050 [C4] L1 スモークの「フォルダ登録→スキャン→グリッド表示」を in-process 代替(必須報告)
- 手法が与えなかったもの: UI 自動操作の手段(フォルダピッカーはネイティブダイアログで自動化不能、UI オートメーション治具は調達外)
- 代替した判断(何をどう埋めたか): 指示書の代替規定に従い、(a) 実 exe の起動・生存・ウィンドウ生成・多重起動防止・正常終了+settings.json/db 生成の確認、(b) 実サービス+実 DB+実画像で登録→スキャン→シェル VM のグリッド行生成→サムネイル生成→NodeGraph 絞り込みまでを通す in-process テスト(`CpL1SmokeTests`)、の組み合わせで実施。画面描画の確認は golden(maintainer)に委ねる
- 重大度: minor

### CHEAT-051 [C2] NodeGraph ルートノードの表示名とセル内画像のフィット方式が未規定
- 手法が与えなかったもの: OC-2 のルートは DisplayName 空。グリッドセル内のサムネイル配置(クロップ/レターボックス)の規定
- 代替した判断(何をどう埋めたか): ツリーのルート表示名=ビュー名(選択=ビュー条件のみの無条件相当)。セル内は Uniform フィット(アスペクト比保持・レターボックス、背景はパネル色)— クロップ(UniformToFill)は採らず
- 重大度: minor

### CHEAT-052 [C5] ThumbnailImage コントロールの static サービス参照
- 手法が与えなかったもの: XAML テンプレート内コントロールから DI サービス(ThumbnailService)へ到達する方法の規定
- 代替した判断(何をどう埋めたか): `ThumbnailImage.Service` static プロパティをコンポジションルート(App)が起動時に設定する方式。VM 層は文字列パスのみ保持し Avalonia 非依存を維持(unit 検査可能性を優先)
- 重大度: minor

**CHEAT 集計: 18 件(blocker 0 / friction 3 / minor 15)**

### 導出メモ(cheat ではなく BOM からの導出と判断したもの)
- `IImageRepository.GetAllNormalAsync` / `ITagRepository.GetAllImageTagsAsync` の追加: INV-010(既定表示=normal のみ)と OC-1 入力構築の供給元(Run 2 導出と同型)
- 多重起動時の既存ウィンドウのアクティブ化は未実装: K-AVALONIA「既存アクティブ化は V1 ではベストエフォート」の規定どおり 2 つ目は即終了
- グリッド仮想化は K-AVALONIA 既定方式どおり「1 行=N セル」の行リスト化+ListBox(VirtualizingStackPanel 既定)。**逸脱なし**
- リスト表示も ListBox(行=アイテム)で仮想化。削除済みタグ列はパース時に除外(REQ-042 の「無視して描画」)
- ログ構成は M-BOM silence_sweep どおり(`%APPDATA%/ViewPrism2/logs/app-.log` 日次 7 世代・Information 以上)。Serilog のファイルは初回イベント書込み時に遅延生成される(起動のみではファイルなし)
- 言語切替の一斉再バインドは LocalizationProxy(インデクサ変更通知)+各 VM の CultureChanged 購読。XAML 直書き文字列なし(K-AVALONIA)
- TextBox の Watermark は Avalonia 12 で obsolete のため PlaceholderText を使用(警告ゼロ要件)
- スキャン中の UI 操作は許可(M-BOM silence_sweep)。スキャンはフォルダ管理ウィンドウから await し進捗バー表示

## 5. blocked 単位

なし。

## 6. 申し送り(設計者向け)

1. **タグ付与 UI が E-BOM に存在しない**: REQ-026/027(付与・解除・バッチ)は core(TagService)で実装・受入済みだが、V1 表面に「画像へタグを付ける」画面が E-BOM 上定義されていない(E-UI-TAGS-026 はタグ定義の管理のみ)。NodeGraph・詳細タグ一覧・タグ列の golden 確認にはタグ付け済みデータが必要(当面はテスト/SQL でのシードで確認可能。`CpL1SmokeTests` が手順例)。次ループで E-UI-TAGGING の追補を推奨
2. 「全画像」固定入口(CHEAT-038)とビューア起動経路=ダブルクリック(CHEAT-039)の E-BOM/仕様反映を推奨
3. display_columns・home_tag_id の編集 UI は未提供(CHEAT-048)。G-2 は既定 3 列または DB 設定値で確認可能、REQ-037 のホームタグ解決はシェル実装済み
4. M-UI-014 の nav シグネチャは絶対パス解決の都合で ImageEntry 入力に変更(CHEAT-044)。M-BOM の interface_contract 改訂を推奨
5. K-DESIGN へのパネル幅・バッジ径・選択背景アルファ等の追補(CHEAT-043)を推奨(golden 往復の削減)
6. SkiaSharp が GIF/BMP エンコード非対応である旨を治具指示(フィクスチャ生成方法)に反映推奨(CHEAT-037)
7. Avalonia.Diagnostics 12.0.4 は引き続き NuGet 未発行(Run 1 CHEAT-002)。調達表改訂まで参照しない
8. golden(CP-UI-G1〜G5)は実アプリ+フィクスチャ画像セットで maintainer の目視承認待ち。確認手順: 同期フォルダ管理→フォルダ追加→スキャン→全画像グリッド(列数・ソート・リスト切替・選択バッジ・ダブルクリックでビューア・詳細ノート保存・設定で ja/en 切替)
