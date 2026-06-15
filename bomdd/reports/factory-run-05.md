# Factory Run 5 報告 — ViewPrism2 ECO-007(タグタブ UI/UX 表示契約 — モック準拠)

- 製造装置: factory-01(隔離工場)
- 実施日: 2026-06-16
- 範囲: ECO-007(E1〜E7)の製造 — タグタブ ビュー行(E1)・タグパレット行(E2)・ノード配置ボタン(E3)・タグ作成/編集ダイアログ付与プレビュー新規(E5)・種別セグメント3タブ(E6)・カラー9色 Radix 化(E7)。E4=確認のみ(変更なし)・ECO-008=現状追認(変更なし)
- baseline: commit 3adc590(ECO-006 適用後)。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変・追加行なし(表示のみ・挙動不変)
- 隔離規律: 遵守 — `tests/ViewPrism2.Oracle/`(中身)・`bomdd/41-fixed-oracle.yaml` は未読・未変更。原典 view-prism は未参照。製造パッケージ(60-change-order-eco-007.md §3 / 20-spec.md §2.6 / 30-ebom.yaml invariants / 33-control-plan.yaml CP-DISPLAY-PARITY-022)+ モック(`work/tag-tab/`)+ 既存 src のみ参照。全テスト(Oracle 含む)の**実行**は回帰確認として実施
- cheat 分類: 既存 Run 慣行を継続 — C1=仕様/契約の欠落補完 / C2=契約の曖昧さ解消 / C4=治具・受入手段判断 / C5=表面の独自判断

## 1. 変更ファイル一覧

### Core(ViewPrism2.Core)
| ファイル | 変更 |
|---|---|
| `Services/TagAssignmentPreviewBuilder.cs` **(新規)** | E5/DC-TAGPREVIEW-001: 付与プレビュー整形器(純粋ヘルパ)。`TagPreviewKind`(Simple/Textual/NumericStar/NumericPlain)・`TagAssignmentPreview`(record)・`TagPreviewChip` を定義。種別別の付与表現(色ドット+名前 / 候補値チップ(先頭=選択)/ ★並び / 数値ラベル+±)を返す。★モード判定=単位"★" かつ span(max−min) 0..9 整数。代表値=中点 round((min+max)/2)。unit 検査可・pixel-exact 不採用 |
| `Services/ViewService.cs` | E1: `GetHierarchyCountAsync(viewId)` 追加(配置タグ数=階層ノード数。閲覧のみ・modified_at 不変) |

### App(ViewProm2.App)
| ファイル | 変更 |
|---|---|
| `ViewModels/TagsTabViewModel.cs` | E1: `ViewRowViewModel` に `TagCount` 追加・`Description` は tooltip 用(空白のみは null 返し tooltip 抑止)・`IsFavorite` はデータ保持(行非表示)。`ReloadViewsAsync` で各ビューの `GetHierarchyCountAsync` を取得し `tagCount` 注入。E3: `AddRootButtonText`/`AddChildButtonText` を汎用ラベル(`hierarchy.placeTagRoot`/`placeTagChild`)へ・対象タグ名を除去。未選択非活性(`CanAddNode`)は維持 |
| `ViewModels/TagPaletteViewModel.cs` | E2: `TagPaletteRowViewModel` から `Description`/`HasDescription` を削除(Tag.Description はデータとして残し、行 VM では非公開) |
| `ViewModels/TagEditorViewModel.cs` | E7: `ColorPresets` を Material-500 18色 → Radix系9色へ置換。E6: `TagTypeOption` に `IsActive`(セグメント選択強調)・`SelectTypeCommand`・`SyncTypeActive`。E5: `Preview`(核ヘルパ呼出)・`PreviewChips`/`PreviewStars`(axaml 用 proxy)・`IsPreviewTextual/Star/NumericPlain/Numeric` を公開。`TagPreviewChipViewModel`/`TagPreviewStarViewModel` record 追加。Name/Color/SelectedType/Min/Max/Unit/PredefinedValues 変化でプレビュー再評価 |
| `Views/TagsTabView.axaml` | E1: ビュー行から★を撤去・タグ数バッジ(`Border.tagCountBadge`)を行末追加・説明は `ToolTip.Tip` 化(行内 TextBlock 撤去)。E2: パレット行の説明 TextBlock を撤去 |
| `Views/TagEditorWindow.axaml` | E6: 種別 ComboBox → セグメント3タブ(`UniformGrid`+`RadioButton.segmentTab` テンプレ・`TypeEditable` で非活性)。E5: 下端固定の付与プレビュー帯(色ドット+名前 / テキスト=候補チップ / 数値=±ステッパ+★並び or 数値ラベル)。E7 のスウォッチは既存 ItemsControl がそのまま新9色を描画。`Window.Styles` に segment/preview スタイル追加 |
| `Assets/i18n/ja.json`・`en.json` | 新規キー5件(両言語): `hierarchy.placeTagRoot`/`hierarchy.placeTagChild`(E3)・`tag.preview.label`/`tag.preview.caption`/`tag.preview.namePlaceholder`(E5)。旧 `hierarchy.addRootNamed`/`addChildNamed` は未使用化(削除せず温存。ja/en 同一キー集合は維持) |

### Tests(ViewPrism2.Tests)
| ファイル | 変更 |
|---|---|
| `CpTagPreview001Tests.cs` **(新規)** | CP-DISPLAY-PARITY-022 / DC-TAGPREVIEW-001: プレビュー整形器の種別別出力を exact 検査(11 ベクタ)。★モード判定(1-5/0-9 境界・span 10 超→プレーン・非整数→プレーン)・テキスト候補チップ(先頭選択・0件)・数値プレーン(%/pt/単位なし)・シンプル・名前プレースホルダ |
| `CpDisplayParity022Tests.cs` | A-4 を ECO-007/E1 改訂版へ(TagCount 公開・★は行非表示=データ保持・Description は tooltip 用 null 抑止)。A-5 を ECO-007/E2 撤回版へ(パレット行 VM に Description/HasDescription が無いことを reflection で検査・色/名前/型は維持) |

## 2. 各 E の実装要点

- **E1(ビュー行)**: ★を行から撤去(`IsFavorite` データは `ViewRowViewModel.IsFavorite` として保持)。配置タグ数バッジ=階層ノード数を `ViewService.GetHierarchyCountAsync` で取得し行末ピルに表示。説明は行内 truncate をやめ、ビュー名+バッジの双方に `ToolTip.Tip` で付与(空白のみは VM が null を返し tooltip を出さない)。
- **E2(パレット行)**: 行 VM の `Description`/`HasDescription` を削除し axaml の説明 TextBlock も撤去。`Tag.Description` はエンティティに残り、作成/編集ダイアログの説明欄(`Description` バインド)でのみ参照。
- **E3(配置ボタン)**: 対象タグ名入りラベル(`「{tagName}」をルートに追加`)を汎用ラベル『タグを配置』/『タグを配置（子）』へ。i18n キー `hierarchy.placeTagRoot`/`placeTagChild` を新設(ja/en)。パレット未選択時は `CanAddNode=false` で非活性+選択を促す文言(`hierarchy.selectTagToAdd`)を維持。
- **E5(付与プレビュー新規)**: 整形を Core 純粋ヘルパ `TagAssignmentPreviewBuilder` に置き unit 検査。表現はモック `work/tag-tab/ViewPrism2 タグ作成ダイアログ.dc.html` の renderVals ロジックに準拠 — シンプル=色ドット+名前 / テキスト=候補チップ(先頭=塗り選択)/ 数値★=単位★かつ span 0..9 整数で★並び+値ラベル / 数値プレーン=±ステッパ+数値ラベル。**実付与 UI(E-UI-TAGASSIGN-029=TaggingPanel/NumericValueDialog)との一致**: 値ラベルは `{値} {単位}`(NumericValueDialog の ConstraintText/INV-007 数値表現と整合)、テキストチップは候補値ドロップダウン(predefined_values 順)に対応、色ドット+名前は ImageTagChip 相当(TaggingPanel CurrentTag 行の Ellipse+Name と一致)。
- **E6(種別セレクタ)**: ComboBox → セグメント3タブ(`UniformGrid` 横並び・`RadioButton` ベースのトグル、丸を隠したタブ視覚)。`SelectedType`/`TypeOptions` バインド。`TypeEditable=false`(既存タグ編集)はコンテナ `IsEnabled=false` で非活性+`SelectType` 内ガード(ECO-008: 種別は作成後変更不可・挙動不変)。
- **E7(カラー)**: `ColorPresets` を9色 `#e5484d #f2912b #e8b931 #30a46c #12a594 #2f6bed #8b5cf6 #e93d82 #5b6473`(Radix系)へ置換。自由 hex 入力(`Color` TextBox)は維持。既存タグの色 hex は不変(プリセットは入力ショートカット)。
- **E4(⌘K)**: タグタブ・アプリ全体に⌘K/グローバル検索 UI は不在を確認(`grep` で該当なし)。新規追加せず — **コード変更なし**。
- **ECO-008(種別ロック)**: `TypeEditable=IsCreate` の既存ロックを維持・挙動不変 — **コード変更なし**(E6 のセグメント非活性で同等担保)。

## 3. build / test 結果

- `dotnet build -c Debug` → **成功(警告 0・エラー 0)**(TreatWarningsAsErrors=true。Oracle プロジェクトもビルド対象でコンパイル成功 — 中身は未読)。経過 10.8s
- `dotnet test -c Debug` → **全件 PASS**
  - `ViewPrism2.Tests.dll`: 合格 406 / 不合格 0 / スキップ 0
  - `ViewPrism2.Oracle.dll`: 合格 74 / 不合格 0 / スキップ 2(固定オラクル S-01〜S-31 退行ゼロ)
  - 新規 unit: `CpTagPreview001Tests`(11)+ `CpDisplayParity022Tests` の A-4/A-5 改訂。既存 unit は全件退行なし(★/パレット説明の旧表示を前提にしていた A-4/A-5 のみ ECO-007 契約へ改訂)

| CP | depth | 件数 | 結果 | 被覆 |
|---|---|---|---|---|
| CP-DISPLAY-PARITY-022(E5 新規) | unit | 11 | PASS | ★モード判定(1-5→★3点灯/0-9境界/span10超→プレーン/非整数→プレーン)・テキスト候補チップ(先頭選択・0件)・数値プレーン(%50/pt5/単位なし50)・シンプル(値表現なし)・名前空白→プレースホルダ |
| CP-DISPLAY-PARITY-022(E1/E2 改訂) | unit | 2 | PASS | A-4: ViewRow=TagCount公開・★データ保持(行非表示)・Description tooltip(空白null) / A-5: パレット行 VM に Description/HasDescription 無し(reflection)・色/名前/型は維持 |
| 既存全 CP(Run 1〜v4.0) | unit/L2/L3/golden(unit部) | 393 | PASS | 退行なし |

## 4. cheat / 工場判断(仕様で決めきれず判断した点)

### CHEAT-067 [C5] プレビューの代表値(数値型の表示値)— 必須報告次元
- 与えられなかったもの: プレビューの数値表示は「画像に付けたときの見え方」だが、付与前ダイアログには実値が無い。どの値を代表表示するか仕様未規定。
- 判断: モックの `preset()` と同じ **中点 `round((min+max)/2)`**(min/max にクランプ)を代表値とした。★モードは v≦代表値 を点灯(モック `on = v <= numValue` 準拠)。プレーンは `{代表値} {単位}`。
- 重大度: minor

### CHEAT-068 [C2] テキスト候補チップの「選択強調」対象
- 与えられなかったもの: 付与前で実選択値が無いため、どのチップを選択強調するか仕様未規定。
- 判断: モック既定(`textValue` 初期=先頭候補)に倣い **先頭候補を選択(塗り)**、以降は未選択(枠線)。候補0件はチップ空。
- 重大度: minor

### CHEAT-069 [C5] 数値プレビューの "±ステッパ" の操作性
- 与えられなかったもの: プレビュー帯の ± ステッパ(モックは onClick で numValue を増減)を実プレビューで操作可能にするか。
- 判断: プレビューは**表示専用**とし ± ボタンは `IsEnabled=false`(視覚要素として配置・操作不可)。プレビューは保存値と独立な「見え方」例示であり、実値操作は画像タブ実付与 UI(NumericValueDialog)が担う、という分担を維持。
- 重大度: minor

### CHEAT-070 [C5] セグメントタブ/プレビュー帯の寸法・配色(K-DESIGN 未定義次元・全列挙)
- 与えられなかったもの: E6 セグメント・E5 プレビュー帯の具体寸法。K-DESIGN に新規定義なし。
- 判断: セグメント=コンテナ角丸10/Padding3・選択タブ=白背景+影+アクセント文字。プレビュー帯=上境界線+PanelBrush背景・色ドット13px・名前15px SemiBold・チップ角丸8/Padding9,3・★18px(点灯=AccentBrush/非点灯=BorderLineBrush)・数値ラベル15px Bold。タグ数バッジ=BorderLineBrush背景・角丸9・MinWidth22。モックの寸法(28px swatch・44px input 等)は Avalonia 既存トークン(16px swatch・44px 行高慣行)へ丸めた。pixel-exact は golden 承認に委ねる。
- 重大度: minor

### CHEAT-071 [C2] E3 で root/child の 2 ボタン構成を維持
- 与えられなかったもの: モック M3 は配置を D&D 主導線とし「タグを配置」は汎用エントリ(単一)。既存実装はルート追加/子追加の2ボタン。ECO-007 §3 は「活性/非活性は配置動線設計に委ねる」とし単一化までは指示しない。
- 判断: 既存の2ボタン構成(ルート/子)を維持しつつ、両ラベルを汎用化(『タグを配置』/『タグを配置（子）』)し対象タグ名を除去。D&D 主導線(既存)も併存。単一ボタン化は構造変更を伴うため見送り(golden で動線確認を要望)。
- 重大度: minor(§5 申し送り 1)

### 導出メモ(cheat ではなく BOM/モックからの導出)
- ★モード判定式(単位★・span 0..9・整数)はモック renderVals の `starMode` 定義そのまま。数値ラベルの単位連結 `{値} {単位}` は NumericValueDialog の ConstraintText 表現(INV-007 InvariantCulture)と整合。
- 旧 i18n キー `hierarchy.addRootNamed`/`addChildNamed` は削除せず温存(ja/en 同一キー集合の CP-I18N-010 資産検査を壊さない・将来再利用余地)。
- E5 ヘルパを Core に置いた根拠: §3 DC-TAGPREVIEW-001「整形は核側ヘルパで unit 検査可能」+ ConditionSummaryFormatter(既存 GF-05 核ヘルパ)と同型。

**CHEAT 集計: 5 件(blocker 0 / friction 0 / minor 5)**

## 5. 残 golden 項目(視覚承認待ち — 承認者 maintainer)

CP-UI-G6(20-spec.md §2.6 G-6・ECO-007 追記分)の視覚承認が未消化。unit/契約は緑だが、見え方は golden で確定:

- **E1**: タグタブ ビュー行に★が出ない・タグ数バッジが表示される・説明は hover tooltip(行内に出ない)
- **E2**: タグパレット行に説明が出ない(色+名前+型のみ)
- **E3**: 配置ボタンが汎用ラベル『タグを配置』(対象タグ名なし)・パレット未選択時は非活性。**D&D 主導線で対象選択**を併せて確認(GF-04 撤回=機能後退でなく配置動線設計変更の確認)
- **E5**: タグ作成/編集ダイアログ下部の付与プレビューが、画像タブ実付与 UI(★/数値±/候補チップ)と表示一致すること(シンプル=色ドット+名前 / テキスト=候補チップ先頭選択 / 数値★=★並び / 数値プレーン=±+数値)
- **E6**: 種別セレクタがセグメント3タブ(シンプル/テキスト/数値)・既存タグ編集時は非活性
- **E7**: カラープリセットが9色(Radix系)で表示・自由 hex 入力が機能

確認手順例: タグタブ → 「追加」でタグ作成ダイアログ(種別セグメント切替→各種別でプレビュー連動・カラー9色選択・テキスト候補追加→チップ・数値 min1/max5/unit★→★並び・min0/max100/unit%→数値±)→ 保存 → パレット行(説明なし)→ パレットでタグ選択→中央「タグを配置」活性 → ビュー一覧(★なし・タグ数バッジ・説明 tooltip)→ ja/en 切替で文言反映。

## 6. 申し送り(設計者向け)

1. **E3 の2ボタン維持(CHEAT-071)**: 「タグを配置」を単一エントリ化(D&D 主導線一本化)するか、ルート/子の2ボタンを残すかの裁定を要望。本 Run は後者(ラベル汎用化のみ)。
2. **プレビュー代表値=中点(CHEAT-067)**: 数値プレビューの代表値選択を仕様化推奨(中点 round 採用)。golden で見え方を確認。
3. **K-DESIGN 追補候補(CHEAT-070)**: セグメントタブ・プレビュー帯・タグ数バッジの寸法/配色・E7 の9色 Radix トークン — golden 往復削減のため K-DESIGN への追記を推奨。
4. **旧 i18n キー**: `hierarchy.addRootNamed`/`addChildNamed` は未使用化。資産掃除のタイミングで削除可(本 Run は ja/en 同一キー集合維持のため温存)。
5. golden(CP-UI-G6・ECO-007 分)は maintainer の目視承認待ち(§5)。
