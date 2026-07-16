# Change Order — ECO-102(implemented): 未保存編集中のタグ定義変更が中央ペインへ反映されない — dirty ガードによる行 Tag 参照の失効

- 起票: 2026-07-17(maintainer ソースレビュー所見・未 push 12 コミットのレビュー)
- 種別: 不具合(タグ定義変更の反映漏れ。既存構造〔2026-06-11 初期製造〕+ECO-099/100 の同族増分)
- baseline: main `f598b3e`

## 1. 症状(報告・2026-07-17 maintainer レビュー。2026-07-17 コード実測で確認済み)

**[P2]** 階層エディタが未保存編集(dirty)の間にパレットでタグ定義を編集(改名・色・数値範囲)しても、
中央ペインの既存ノード表示が旧のまま(名前・色ドット・種別文言・数値メタが失効):

- `TagsTabViewModel.OnTagsChangedAsync:259` は辞書(`_tagById`/`_numericMetaByTagId`)とパレットを更新
  するが、`Editor.IsDirty` の場合(`:270` ガード)エディタを再ロードしない → 既存 `EditNodeViewModel` の
  `Tag` 参照・`NumericMeta` が古いオブジェクトのまま。
- **保存後も再ロードされない**: `HierarchyEditorViewModel.SaveAsync` は `_view` 再取得と dirty 解除のみで
  ノードの Tag 参照を更新しない → 失効表示は**ビュー切替まで持続**する。
- 同族(ECO-099 増分): 配置モード中の `PlacingTag` も古い Tag オブジェクトのまま(配置中にそのタグを
  改名すると帯文言が旧名・その状態で配置すると新規ノードも旧 Tag 参照で生成される)。

## 2. 工程診断 — CAD 沈黙・実装欠陥(表示失効)と確定。gate① 不要(是正選択肢は §4 に記録)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | **沈黙(欠陥ではない)** | tag_tab.md は「dirty 中のタグ定義変更の反映方針」を規定しない(mock は静的データ)。ただし「タグ=グローバル定義・行は参照表示」の契約(REQ-021 系)からは最新定義の表示が自明の期待。 |
| BOM | **健全** | E-UI-NODEGRAPH-025 の表示契約(色ドット/型チップ/数値メタ)自体は正。「タグ変更時の同期」次元が CP に無い=谷間(fix 時追補)。 |
| 実装 | **欠陥と確定(変更対象)** | dirty ガードが**構造の保護**(未保存ツリーを壊さない=正当)と**表示の鮮度**(Tag 参照の差し替え=保護不要)を区別せず一括スキップしている。 |

- **混入コミット**: dirty ガード= `c103967`(2026-06-11・Phase 5 初期 UI 製造)— **潜伏 1 か月超**。
  `NumericMeta`/`PlacingTag` の同族増分= ECO-099 `e8bb277`/ECO-100 `7ef1a00`(2026-07-16)。
- **マスキング要因**: dirty 中にタグ編集へ行く往復操作が golden 手順に無い。非 dirty 時は全再ロードで
  正しく追随するため通常操作では見えない。ECO-046(U-a)は「dirty 中のタグ**削除**」だけを掃射し、
  **編集**の反映は検査面に無かった(削除ガードと同じ谷間の別面)。
- 未確定事項との関係: なし。

## 3. 切り分け済みの事実(2026-07-17 コード実測)

- `EditNodeViewModel.Tag` は `{ get; }`(生成時固定)。表示(DisplayName/Color/RingColor/TypeText/
  Is* 型判定)は全てこの参照経由= 参照が古ければ全表示が失効する構造。
- dirty ガードの意図は**未保存ツリー構造の保護**(再ロードすると編集が消える)であり正当。問題は
  表示鮮度まで一緒に犠牲にしている点。
- タグ**削除**時は DB CASCADE+ECO-045/046 ガードが別途統制(本件と独立・回帰させない)。
- **未検証(fix 時に確定)**: 参照差し替えの波及面の全数(パレット行・帯・挿入時の Tag 引き渡し・
  ConditionSummary の値表示に Tag 依存が無いか)。

## 4. 是正方針(案・着手時確定。案 A を既定=真因構造を消す)

- **案 A(既定)**: 「構造の保護」と「表示の鮮度」を分離する。`EditNodeViewModel.Tag` を差し替え可能にし
  (または表示解決を辞書経由に変更)、`OnTagsChangedAsync` で dirty でも**Tag 参照・NumericMeta だけ**
  最新辞書から再束縛(ツリー構造・別名・条件・展開・HOME・dirty 状態は不変)。`PlacingTag` も同 id の
  最新 Tag へ差し替え(消えたら解除=既存)。非 dirty 時の全再ロードは現状維持。
- 案 B(対症): 現状維持+保存成功時のみ再ロード。dirty 継続中の失効が残るため不採用方向。
- **プローブ(R5)**: dirty エディタで配置済みタグを改名・色変更・数値範囲変更 → 該当ノードの
  DisplayName/Color/NumericMeta が即追随(是正前赤)・IsDirty/ツリー構造/別名/条件/HOME 不変・
  配置中 PlacingTag の帯文言追随。保存回帰(改名後の保存で階層無傷)。
- **CP 追補**: CP-UI-G6 へ「dirty 中のタグ定義変更の反映(構造保護と表示鮮度の分離)」を潜伏実績つきで
  追加(ECO-046 の削除面と対で編集面を塞ぐ)。

## 5. 影響 BOM

- **src(案 A)**: `HierarchyEditorViewModel`(Tag 再束縛 API= RebindTags(tagById)級+EditNode の
  Tag 差し替えと表示プロパティ再通知)+`TagsTabViewModel.OnTagsChangedAsync`(dirty 分岐で再束縛呼び出し)。
  XAML/style/i18n/CAD/DB= 変更なし見込み(視覚は「最新定義になる」だけ)。
- **tests**: 新規 probe(§4)。既存 CpUiG6 系全緑維持。R6= 固定 Oracle 不変。
- **CP**: CP-UI-G6 追補。
- **E-BOM**: E-UI-NODEGRAPH-025 へ「タグ定義変更の反映=構造保護と表示鮮度の分離」invariant 追補。

## 6. 残ゲート

- gate①(裁定): **不要**(案 A= 最新定義の表示は契約の自明解釈・maintainer レビューが欠陥と指摘済み。
  案の分岐は本文に記録済みで、着手時に案 A で確定予定 — 異論があれば起票時点で裁定可能)。
- gate②(golden): **必要**。基準案: dirty 編集中にパレットで配置済みタグを改名/色変更 → 中央ペインの
  該当行が即追随・編集ツリー(構造/別名/条件/HOME/dirty)は不変・保存で正常永続・非 dirty 時の
  従来挙動回帰なし。

## 7. `/eco-fix` 実施記録(2026-07-17)

### 7.1 プローブ先行(R5)

新規 `CpUiG6DirtyRebindTests`(4 本)— 既存公開 API(TagService 更新+パレット編集完了経路
Palette.EditCommand→TagsChanged)だけで組めたためスタブ不要の純粋な赤: 是正前 **3/4 不合格**
(dirty 中の改名/色=旧表示・数値範囲=旧メタ・配置中改名=帯が旧名)。残 1 本= 非 dirty の
従来経路 pin(全再ロード=もともと正・回帰検知用)。

### 7.2 是正内容(案 A=構造の保護と表示の鮮度の分離)

- `EditNodeViewModel.RebindTag(tag, numericMeta)`: Tag 参照と数値メタを差し替え、派生表示
  (DisplayName/色/リング/型/条件可否/メタ)を一括再通知。構造プロパティ(Children/Alias/条件/
  展開/IsHome)には触らない。
- `HierarchyEditorViewModel.RebindTags(tagById)`: 全行を最新辞書へ再束縛+`PlacingTag` も同 id の
  最新オブジェクトへ差し替え(消えていたら配置解除=ECO-099 既存ガードと同じ帰結)。
- `TagsTabViewModel.OnTagsChangedAsync`: 非 dirty= 従来どおり全再ロード(不変)/ dirty(else)=
  `RebindTags` を呼ぶ。SaveAsync は変更不要(dirty 中に鮮度が保たれるため保存後の失効自体が消滅)。
- **横断規約(ECO-080)**: 新文言なし・XAML/style/i18n/DB 不変。

### 7.3 機械受入

build 0 error / **Tests 782/782**(プローブ 3 本緑転+pin 1 本・既存 778 不変)/ Oracle 109+2skip
(R6 不変)/ validate_bom 0/0。**R7= 対象外**(XAML/style 不変・表示が最新定義になるだけ=
ECO-096/098 前例)。M4= E-UI-NODEGRAPH-025 へ「構造の保護と表示の鮮度の分離」invariant+
CP-UI-G6 へ dirty 中タグ編集反映の次元(潜伏実績つき)。

## 8. 残ゲート(更新)

- gate②(golden)のみ。合格基準は §6 のとおり(dirty 編集中の改名/色変更が中央ペインへ即追随・
  編集ツリー不変・保存正常・非 dirty 従来挙動の回帰なし)。
