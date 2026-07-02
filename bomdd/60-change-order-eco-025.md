# ECO-025: 表示列モデル(ビュー編集モーダル + ファイル一覧)— ビュー.columns[] 進化モデル + 2 surface(CADモック由来・段階 α/β)

- **status**: staged(起票 + E-BOM(30)/REQ(10)同期。spec §2.6/§2.x・M-BOM・Control Plan の全面同期と実装は製造フェーズ=α/β・M4)
- **type**: 設計決定(既存ドメイン属性 display_columns へ進化モデル付与 + 新編集/描画 surface・CADモック由来)。要件は ECO-020/022 と同型の CAD→ECO→E-BOM フロー
- **baseline**: ECO-024 適用後(main `c100097`)
- **bom_rev**: v4.0(eco:ECO-025)
- **CAD 入力(一次)**:
  - `ViewPrismUI:資料/タグタブ/ViewPrism2 ビュー編集 (standalone).html`(α=ビュー編集モーダル・DC standalone)
  - `ViewPrismUI:資料/画像タブ/ViewPrism2 ファイル一覧 (standalone).html`(β=リスト表示・DC standalone)
  - CAD(原器): `ViewPrismUI:docs/screens/view_edit.md` / `docs/screens/file_list.md`
  - 併読: `docs/screens/tag_tab.md` / `image_tab.md` / `docs/01_design_direction.md` / `docs/review_points.md`(VE-001〜004・FL-001/002・IMG-001/007/013)
- **乖離時の権威**: 常に ViewPrismUI が正(`docs/02_mock_fidelity_policy.md` P3)

## 1. 背景 — なぜこの ECO か

タグタブの「ビュー」は 2 属性を持つ: **タグ階層(潜る軸)**(既存・E-UI-NODEGRAPH-025)と、**表示列(見せる軸)**。後者を画像タブ ファイル一覧(リスト表示)へ渡す列構成として設計原器化したのが本 ECO。ViewPrismUI で 2 画面を CAD 化し(view_edit / file_list)、設計裁定(VE-001〜VE-004・FL-001/FL-002)も maintainer が 2026-07-02 に確定済み。

- 上流(タグタブ): ビュー編集モーダルが `display_columns` を編集する(α)。
- 下流(画像タブ): ファイル一覧がアクティブなビューの `display_columns` をテーブル列・ソート軸として描画する(β)。

## 2. 既存資産の整合(現状追認・捏造しない)— `display_columns` は net-new スキーマではない

CAD は「ビュー.`columns[]` を net-new ドメイン属性」と表現するが、**スキーマ・Core・描画は V1 から既存**であることを撤去前に確認した(ECO-024 §2 と同じ依存検証規律)。

| レイヤ | 現状(V1 既存) | 出所 |
|---|---|---|
| DB スキーマ | `views.display_columns TEXT NULL` | `DatabaseSchema.cs:81` / `V0SchemaFixture.cs` / `CpDb006Tests.cs` |
| エンティティ | `View.DisplayColumns : string?` | `Core/Models/Entities.cs:137` |
| Core サービス | `ViewService.CreateAsync(displayColumns:…)` | `Core/Services/ViewService.cs:31,48` |
| リポジトリ | `display_columns` の INSERT/UPDATE/SELECT ラウンドトリップ | `Infrastructure/Database/ViewRepository.cs` |
| 描画パーサ | `DisplayColumnParser`(`{type: basic|tag, key, label, width}`・既定 3 列 name/size/modified_date・削除タグ列は無視) | `App/ViewModels/DisplayColumnParser.cs` |
| 要件 | REQ-030(View CRUD に `display_columns` 含む)・REQ-042(リスト列は `display_columns` に従う・既定 3 列) | `10-requirements.yaml` |
| 受入 | CP-VIEW-012(`display_columns` basic+tag ラウンドトリップ)・CP-UI-G2(列解釈) | `33-control-plan.yaml:153` / `CpView012Tests.cs` / `CpUiG2ColumnsTests.cs` |
| spec | §2.6 リスト(REQ-042) 列 = `{type,key,label,width}` | `20-spec.md:315` |

**帰結**:
1. **DB マイグレーション不要**(ECO-020 の workspaces 追加と対照的)。進化モデルは同一の `views.display_columns TEXT`(JSON)へ前方互換に直列化する。スキーマ同値(CP-DB-006)不変。
2. 一方で **V1 には編集 UI が無い**(factory-run-03 CHEAT-048=`display_columns`/`home_tag_id` の編集 UI 未提供。値は DB 直接設定でのみ変わる)。**ビュー編集モーダルは net-new surface**(α)。
3. V1 の描画契約は `kind` が `basic|tag` の 2 値のみで、**列ヘッダーソートも型別セル描画(num/text/simple)も無い**。**ファイル一覧の列ヘッダーソート + 型別セル描画は net-new**(β)。

したがって本 ECO の net-new は「スキーマ」ではなく、(a)`display_columns` へ与える**進化モデルと不変条件**(順序・最大5・名前固定・基本∪ビュー内タグ・kind・ビュー所有)、(b)**ビュー編集モーダル surface**、(c)**ファイル一覧の列描画契約 + 列ヘッダーソート不変条件**の 3 点。

## 3. net-new / 変更(E-BOM に反映)

### 3.1 表示列 進化モデル(REQ-079・E-VIEWSVC-009)

`display_columns` は順序付きの列配列で、各列は:

- `source ∈ {basic, tag}`・`key`・`label`・`kind ∈ {basic, num, text, simple}`・(tag は `color`)・`width`。
- 基本情報キー = `name`(=c_name) / `size` / `modified_date`。タグキー = タグ id。
- `kind` は描画とソート規則を決める(basic は name/size/date、tag は タグ型 → num=数値 / text=テキスト / simple=シンプル に射影)。

不変条件:

- **VE-001 名前固定**: `columns[0]` は常に `c_name`。先頭固定・削除/移動不可・「固定」バッジ。両画面共通。**モック是正**(ビュー編集モックは名前を通常列扱い=安全/整合はモックより上位でロックする)。
- **VE-002 上限**: 列数は `1 ≤ N ≤ 最大`(標準 5・固定で先行)。名前固定により下限は常に 1 以上。DC プロップの可変レンジ(min2/max8・min3/max7)は製造仕様でない。
- **VE-003 所有**: `display_columns` は**ビュー定義が所有**。コレクション×ビューのローカル上書きを持たない。ファイル一覧の「表示列」編集も同一の `display_columns` を書き戻す(別コレクションで同ビューを開いても同じ列構成)。
- **列候補の母集合**: 選べるタグ列は**そのビューのタグ階層メンバーシップに含まれるタグに限る**(view_edit / file_list 共通)。
- **Ver1「種類(kind 列)」は廃止**: 列ソースは基本情報とタグのみ。
- `modified_at` は `display_columns` 変更でも現在時刻へ更新(REQ-032・閲覧では不変)。

### 3.2 α — ビュー編集モーダル(REQ-080・E-UI-TAGS-026 拡張)

タグタブの「ビュー管理」行から開くモーダル。ビュー名・説明 + **表示列の構成**。表示列エディタ = **列ピッカー**:

- 選択済み列を順序表示(各行に上下移動・削除・ソースチップ[基本=グレー/タグ=青]・タグ色ドット)。
- 追加元カード: 基本情報(破線カード)/ タグ(実線カード・種別チップ+色ドット)。クリックで**末尾追加**(上限/重複は無視)。
- 件数バッジ `合計 N 列 / 最大 M`。上限到達で青→アンバー、追加元カード不活性(`not-allowed`・淡色)。
- 名前は先頭固定・移動/削除不可・「固定」バッジ(VE-001)。
- 空状態: 追加元に基本情報/タグが無いとき各セクションに案内。端の列で上下矢印不活性。
- 保存で `display_columns` をビュー定義へ書き戻し(VE-003)、ファイル一覧へ即反映。

### 3.3 β — ファイル一覧(リスト表示)列描画 + ソート(REQ-081・E-UI-BROWSE-022 拡張)

アクティブなビューの `display_columns`(3.1 モデル)を**テーブル列の描画契約**とする。

- **型別セル描画**: basic(size=`X.X MB`/`X KB`・date=日付) / num=`★`×値+数値(金色・未設定=淡色) / text=値チップ(タグ色淡塗り+境界・未設定=「—」) / simple=色ドット+ラベル(付与)/オフ(未付与)。
- 名前は固定列(先頭・削除/移動不可・「固定」バッジ)。列テンプレートは名前=伸縮(`minmax`)・他=固定幅。
- **列ヘッダー = ソートトグル**: 別列クリック=昇順開始、同列再クリック=昇順⇄降順、ソート概要「クリア」で解除。
- **ソート不変条件**(描画から独立した決定論ロジックとして unit 検査可能・E-UI-BROWSE-022 の選択ロジックと同じ規律):
  - 未設定タグ行は方向に関わらず**常に末尾**(空値末尾)。ただし simple は空扱いにせず**有無順**(付与を先・昇順時)。
  - 型別比較: 数値タグ / basic size = 数値順。テキストタグ / basic name・date = 文字列順(`localeCompare('ja')`)。simple = 有無順。
  - 同値のタイブレークは**名前の昇順**で安定化。
- 「表示列」ポップオーバーで列を追加/削除/並べ替え(view_edit と**同一の列ピッカー**・`display_columns` をビュー定義へ書き戻す VE-003・除去列がソート中ならソート解除)。上限到達でバッジ アンバー・追加元不活性。

## 4. 設計裁定(maintainer 2026-07-02・review_points.md に集約)

| ID | 裁定 | 本 ECO での扱い |
|---|---|---|
| VE-001 | 名前列は固定(先頭・削除/移動不可・「固定」バッジ)を両画面の正。モックは是正でロック | 先行(3.1/3.2/3.3 不変条件) |
| VE-002 | 最大列数=標準 5・固定で先行(DC 可変レンジは製造仕様でない) | 既定値で先行(下限/上限の可変化は未確定=保留) |
| VE-003 | `display_columns` はビュー定義所有。ローカル上書きなし。ポップオーバー編集は書き戻り | 先行(E-VIEWSVC-009 所有) |
| VE-004 | 空のビュー名で保存を許可するか | **未確定**(β/実装で確定・既定=必須で先行可) |
| FL-001 | グリッド/リスト間の列・ソート共有 | **未確定**(表示列モデルはリスト前提。グリッドは左下タグドットで列概念なし) |
| FL-002 | ソート状態(列・方向)の永続範囲 | **未確定**(既定=画面ローカルで先行可) |

## 5. 段階分割(work_tab の α/β と同じ運用)

- **α**: ビュー.`display_columns` の進化モデル E-BOM 化(3.1)+ ビュー編集モーダル(3.2・名前ロック込み)。
- **β**: ファイル一覧のリスト列描画 + 列ヘッダーソート(3.3・不変条件込み)。
- **列ピッカーの DRY**: α/β は各 surface が**列ピッカーの独立インスタンス**を持つ(isolate now)。両 surface が golden 済みになった後に**共通部品 SC-COLUMN-PICKER-001 へ DRY 統合**(35-design-system-bom 候補・ECO-021 の「isolate now, DRY later」と同じ運用)。

## 6. 影響 BOM(E-BOM(30)/REQ(10) 同期。spec/M-BOM/CP は製造フェーズ・M4)

| 対象 | 是正 |
|---|---|
| REQ-079(新設) | 表示列 進化モデル(順序・最大5・名前固定・基本∪ビュー内タグ・kind・ビュー所有・種類廃止)。VE-001/002/003 |
| REQ-080(新設) | ビュー編集モーダル(表示列構成エディタ = 列ピッカー・名前固定バッジ・件数/上限バッジ) |
| REQ-081(新設) | ファイル一覧 列描画契約(kind 別セル)+ 列ヘッダーソート不変条件(空値末尾・型別比較・安定タイブレーク) |
| E-VIEWSVC-009 | 進化モデルの所有/不変条件(VE-001/002/003・タグ母集合・kind・種類廃止・modified_at)。requirement_refs += REQ-079。acceptance=CP-VIEW-012(既存ラウンドトリップ) |
| E-DB-010 | 現状追認: `views.display_columns TEXT` 既存=**マイグレーション不要**。進化モデルは同一 TEXT/JSON へ前方互換直列化。スキーマ同値(CP-DB-006)不変 |
| E-UI-TAGS-026(α) | ビュー管理から開くビュー編集モーダル surface(列ピッカー・名前固定)。external_source_ref に ビュー編集モック追記。requirement_refs += REQ-079/080 |
| E-UI-BROWSE-022(β) | リスト列描画契約(kind 別セル)+ 列ヘッダーソート(不変条件・unit 検査可能)+ 表示列ポップオーバー(列ピッカー・書き戻り)。requirement_refs += REQ-081 |
| 35-design-system-bom | SC-COLUMN-PICKER-001(列ピッカー共有部品候補・DRY 統合先)を追加(candidate・両 surface golden 後に採用) |
| 60-change-register | ECO-025 エントリ追加 |

**触れない**: E-UI-NODEGRAPH-025(タグ階層エディタ=別軸・スコープ外)・E-UI-AXIS-NAV-040(アクティブビュー供給は既存・列は E-VIEWSVC-009 経由)・E-SORT-004(画像一覧の sort_field 整列とは別軸=列ヘッダーソートは β surface の決定論ロジック)。

## 7. 受け入れ基準(02_mock_fidelity_policy.md)

- モックの主要フロー再現(列の追加/削除/並べ替え/上限/名前固定・列ヘッダーソート・型別セル)。
- 状態欠落なし(上限到達・空・未設定・選択・端の列・ソートなし)。
- 画面密度・行高が大きく変わらない(標準 52px / コンパクト 42px)。
- 列ピッカーの手触りが両画面で揃う(DRY 前でも挙動同一)。
- モックと違う判断(名前ロック=VE-001)の理由が CAD に残っている(view_edit.md「名前列の扱い」・本 §2/§4)。

## 8. 検証(本 ECO=staged の範囲)

- `python bomdd/validate_bom.py`: 0 error / 0 warning(E-BOM dangling・core/surface 規律・register 語彙・manifest↔register 整合)。
- Oracle/Tests: 本 ECO はコード非製造(staged)につき不変(S-01〜S-31・既存 Tests)。α/β 製造時に unit(列ピッカー/ソート比較器の決定論ベクタ)+ golden(承認者 maintainer)で受入。
- スキーマ不変(display_columns 既存・マイグレーション無し)。

## 9. pending_sync(製造フェーズ・M4)

- **α 製造**: ビュー編集モーダル(E-UI-TAGS-026)実装 + 列ピッカー unit + golden。
- **β 製造**: ファイル一覧 列描画 + ソート比較器(unit・空値末尾/型別/タイブレークの決定論ベクタ)+ 列ヘッダー golden。
- **DRY**: 両 surface golden 後に SC-COLUMN-PICKER-001 へ列ピッカー統合。
- **spec §2.6(タグタブ ビュー編集)/§2.x(リスト列・ソート)・M-BOM 製造トレース・Control Plan** の全面同期。
- **未確定の確定**: VE-004(空名保存)・FL-001(グリッド/リスト共有)・FL-002(ソート永続範囲)。

## 10. 採番メモ

ユーザー起票時の想定 ECO-024 は既に「画像タブ 原典撤去」で使用済み(register)。採番規則(逐次)に従い本 ECO は **ECO-025** で確定。

## 11. 実装状況

- **α = 製造済(golden pending)**:
  - `ViewColumnModel`(App/ViewModels・純粋/決定論): 進化モデル(名前固定 VE-001・最大5 VE-002・タグ母集合限定・kind・display_columns 直列化=DisplayColumnParser 互換)。
  - `ViewEditDialogViewModel` / `ViewEditDialog.axaml`: ビュー編集モーダルに列ピッカー(選択済み列+上下移動/削除+ソースチップ/色ドット+追加元カード[基本=破線/タグ=実線+種別チップ]+件数バッジ[上限アンバー]+名前固定バッジ)を追加。
  - `WindowService.ShowViewEditDialogAsync`: ビューのタグ階層メンバー(`GetHierarchyAsync`→`Tag`)を母集合として供給。
  - loc(ja/en 13 キー)。テスト `CpViewColumnModelTests` 11 件。
  - 検証: build 0/0(Debug/Release・TreatWarningsAsErrors)/ Tests 485(+11)/ Oracle 100+2skip(退行ゼロ)/ validate_bom 0/0。
  - **α golden 反復(maintainer 実機)GF-1〜5 是正済**: GF-1 お気に入り撤去(廃止仕様)/ GF-2〜3 レイアウトをモック権威へ(フッター下部 docked・本体スクロール・追加元2カラム)/ GF-4 単一スクロール化(mock是正=二重スクロール解消)/ GF-5 スクロールバー inset(内容 Margin・K-AVALONIA)。
- **β 列描画+ソート = 製造済(golden pending)**: `ViewColumnSorter`(ソート不変条件)+ `ListColumnBuilder`(display_columns→列定義+型別セル)+ `ImageTabView` 配線(動的列テーブル=workspace型 sticky ヘッダー・名前 1.7* 伸縮・`GridColumnsBinder` で列位置一致・kind 別セル・列ヘッダーソートトグル・ソート概要クリア)。テスト CpViewColumnSorterTests 6 + CpListColumnBuilderTests 9。検証 build 0/0・Tests 500・Oracle 100+2skip・validate_bom 0/0。**残 = β 視覚 golden(maintainer 実機)+ 表示列ポップオーバー(β-2 残・モーダル型・列ピッカー再利用=両 surface golden 後に SC-COLUMN-PICKER-001 へ DRY)**。

## 12. レイアウト不変条件を実装契約化(golden retro・maintainer 2026-07-02)

α golden の GF-1〜5 の根本原因は **UI-IR/プロース CAD がレイアウト不変条件を抽出していなかったこと**(S3=データ分散レイアウト欠陥・GF-V1 同型。CAPA は方法論に存在したが本画面 CAD へ未適用=横展開漏れ)。BOM/製造計画は妥当・golden は所見を捕捉(計画は正常機能)。

**ViewPrismUI(CAD 権威)側で是正済(maintainer 2026-07-02)**:
1. `docs/screens/view_edit.md` / `file_list.md` に「レイアウト不変条件」節を追記(モーダル=ヘッダ固定/本体スクロール/フッター docked 常時可視/単一スクロール/可変兄弟がフッターを押し出さない・リスト=列ヘッダー固定/名前列/密度)。
2. GF-4 単一スクロールを **mock是正として記録**(モックは2カラム+タグ内部スクロール・単一スクロール採用の理由=操作性)。
3. お気に入り廃止を明記。
4. `docs/templates/screen_spec_template.md` に「レイアウト不変条件」を**必須節**として追加(方法論横展開)。

遡及 backfill は **VP-UI-007 完了**(tag_tab/image_tab/work_tab + viewer 系2画面も名前付き節へ集約=全7画面+テンプレで layoutInvariant 義務化)。**契約は2型**:
- **workspace型**: ペイン境界=スクロール境界・各ペイン単一スクロール・クロム固定(ファイル一覧リスト=画像タブ中央ペイン。列ヘッダー・ツールバー固定で行だけスクロール)。
- **モーダル/全画面型**: サーフェス単一スクロール・docked フッター・GF-V1(ビュー編集モーダル・表示列ポップオーバー)。

**ViewPrismUI が正**=当該レイアウト不変条件節を実装契約として扱う(E-UI-TAGS-026=モーダル型/E-UI-BROWSE-022=リスト workspace型+ポップオーバー モーダル型 の invariant に権威参照+型名を反映済)。GF-5(スクロールバー inset)は Avalonia Fluent 固有のため ViewPrism2 K-AVALONIA 側の実装規約(HTML モックには現れない)。
