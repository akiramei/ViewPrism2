# Design System BOM(候補)— 画像タブ

> **目的**: BomDD `method/ui-ir-ui-bom.md` §9.2 の **Design System BOM ゲート**。画像タブの surface 部品が要求する design parts を列挙し、各 item の `coverage_status` を判定する。`covered`(再利用) または 理由付き `out-of-scope` でない item は製造へ進めない。
> **基盤**: ECO-009 で正典化した部品層 [`src/ViewPrism2.App/Styles/Components.axaml`](../../../src/ViewPrism2.App/Styles/Components.axaml) + `App.axaml` トークン(E-DESIGN-028 / K-DESIGN)。画像タブはこれを最大限再利用し、不足分のみ追加する。
> **対応**: 本書は ViewPrism2 に未導入の `35-design-system-bom.yaml`(BomDD テンプレート `method/templates/35-design-system-bom.yaml`)の画像タブ分の代替台帳。E-BOM 改訂 ECO 起票時に正式な 35 へ昇格してよい。
> **状態**: candidate / 未実証。`new` 部品はモック準拠で追加する設計負債(製造前にここで可視化)。

## 凡例

- **covered** — 既存 Components.axaml クラス/トークンをそのまま再利用。新規製造不要。
- **extend** — 既存クラスの variant 追加で対応(小さな新規)。
- **new** — 画像タブ用に新規追加が必要な surface 部品(製造対象)。欠落すると実機が素 panel/text へ退化する(`design_system_part_missing` リスク)。
- **out-of-scope** — 本スコープ外(理由付き)。

## カバレッジ台帳

| UI-BOM item | design part | coverage | 再利用/追加先 | K-DESIGN | 欠落時の visual gap |
|---|---|---|---|---|---|
| TMP-UI-CMP-0020 コレクション行カード | row card + lead icon + count badge + 選択面 | **covered** | `Border.navCard`(.selected)+`Border.leadIcon`+`Border.countBadge` | 行/カード/選択淡青 | 素 Border 行に退化(タグタブと不揃い) |
| TMP-UI-OCC-0010 コレクション rail | 折り畳みアイコン列 | **extend** | `Button.iconBtn`+`Border.leadIcon` の縦配置 variant | アイコンボタン | グリフ/素ボタンに退化 |
| TMP-UI-CMP-0021 表示軸セレクタ | dropdown button + メニュー行 | **new** | 新規(ボタン+Popup メニュー・選択チェック) | ドロップダウン/メニュー | 素 ComboBox に退化・二軸の手触りが死ぬ |
| TMP-UI-CMP-0022 ツールバーボタン | secondary button(枠+hover)+ active トグル | **extend** | `Button` 既定 + `.toolbarBtn`/`.toggle` variant(青枠 active) | セカンダリ面/角丸 | 素 Button(灰)に退化 |
| TMP-UI-CMP-0023 ソートコントロール | menu button + 方向トグル(無効状態) | **new** | 新規(メニュー+`Button.iconBtn` 方向・disabled 視覚) | メニュー/アイコンボタン | 素 ComboBox+素ボタン |
| TMP-UI-CMP-0024 グリッド/リスト切替 | セグメント(active 白面+影) | **covered** | ナビ pill と同型(セグメント。`Border`+トグル) | セグメント | 素トグルに退化 |
| TMP-UI-CMP-0025 パンくず | home + › + crumb + 件数 | **new** | 新規(ItemsControl + chevron PathIcon) | パンくず/ナビ | 素テキストの列に退化・戻る操作が不明瞭 |
| TMP-UI-CMP-0026 タグチップ(絞り込み/ナビ) | color dot + label + count pill +(nav)› | **extend** | `Border.condChip` 系 + `Border.countBadge` + `Border.leadIcon`(dot) を chip variant 化 | チップ/カラードット | muted 素テキストに退化(タグタブ条件チップ退化と同型) |
| TMP-UI-CMP-0027 画像グリッドカード | サムネ枠 + 名前 + tag dots + 選択チェック/リング | **new** | 新規(サムネ Border + 選択 overlay)。サムネ=E-THUMB-020 | カード/選択視覚(GF-02) | 素グリッドに退化・選択視覚 GF-02 違反リスク |
| TMP-UI-CMP-0028 画像リスト行 | mini thumb + 名前 + サイズ + 更新日・選択行淡青 | **new** | 新規(DataGrid 行 or 等幅 Grid 行)。選択=`TreeViewItem:selected` 相当の淡青 | 行/選択淡青 | 素 ListBox に退化 |
| TMP-UI-CMP-0029 空タグ注記バー | info アイコン + muted テキスト | **extend** | `Border` + `Border.leadIcon`(info)・既存 help/empty 言語 | 注記/空状態 | 欠落(無表示) |
| TMP-UI-CMP-0030 タグ編集パネル | パネル + ヘッダ + selection pill + × | **extend** | パネル容器 + `Border.accentPill`(枚数)+ `Button.iconBtn`(×) | パネルヘッダ/pill | 素パネルに退化 |
| TMP-UI-CMP-0031 パネルタブ | タブ(active 下線青) | **new** | 新規(TabStrip・下線 active)。タグタブにタブ部品は未製造 | タブ | 素 TabControl に退化 |
| TMP-UI-CMP-0032 現在タグピル | color dot + 名前 + ×・種別色配色 | **covered** | `Border.condChip` + `Button.iconBtn`(×) 相当 | チップ/カラードット | muted テキストに退化 |
| TMP-UI-CMP-0033 タグ追加行 | dot + 名前 +(✓/キャレット/+)・種別グループ見出し | **extend** | `Border.navCard`/`.card` 行 + `Border.typeChip`(グループ見出し)再利用 | 行/種別チップ | 素行に退化・種別配色欠落 |
| TMP-UI-CMP-0034 インライン候補値ピッカー | 候補値チップ列(選択=塗り) | **covered** | `Border.valueChip`(選択 variant 追加) | 候補値チップ | 素ボタン列に退化 |
| TMP-UI-CMP-0035 インライン数値ピッカー | 範囲セル(選択=塗り)+ 現在値 | **extend** | `Border.rangePill`(amber)言語 + セル variant | 範囲ピル/amber | 素数値入力に退化 |
| 種別チップ(グループ見出し) | simple灰/text青/number緑 | **covered** | `Border.typeChip`(.simple/.text/.number) | 種別チップ | 配色欠落 |
| カラードット(全所) | タグ色 + リング | **covered** | `Border.leadIcon` 内 or 専用 dot(タグタブ CMP-0005) | カラードット | 色識別不能 |
| トークン(青/淡青/IBM Plex/44px/角丸/9色) | E-DESIGN-028 | **covered** | `App.axaml`(ECO-009) | 全般 | — |
| 画像ビューア起動 | — | **out-of-scope** | スコープ外(E-UI-VIEWER-024・モック明示) | — | — |
| ⌘K グローバル検索 | — | **out-of-scope** | tag-tab UQ-E4 整合で不採用(再検討 UQ-I08) | — | — |

## ロールアップ

- **covered: 6**(コレクション行・グリッド/リスト切替・現在タグピル・候補値ピッカー・種別チップ・カラードット・トークン)— ECO-009 部品層がそのまま効く。
- **extend: 7**(rail・ツールバーボタン・タグチップ・空注記・編集パネル・タグ追加行・数値ピッカー)— 既存クラスの variant 追加で対応。
- **new: 6**(軸セレクタ・ソートコントロール・パンくず・グリッドカード・リスト行・パネルタブ)— 画像タブ用に新規製造が必要。**製造前に Components.axaml へ追加**し、その場スタイルにしない。
- **out-of-scope: 2**(ビューア・⌘K)。

> **ゲート判定**: `new`/`extend` の 13 部品は製造フェーズで Components.axaml に定義してから surface へ適用する(原則03)。`design_system_part_missing` を避けるため、実装着手前に本台帳の `new` を全て製造計画へ載せること。golden 基準=モック。
> **合意済(2026-06-17・ECO-012)**: DS1 — `new` 6部品は製造前に Components.axaml へ追加で合意。DS2 — `extend` 7部品は既存クラスに variant class を足す(構造が異なるもののみ新クラスに分ける)。

## E-DESIGN-028 / K-DESIGN への含意

- **トークン追加は不要**(青・淡青・9色・IBM Plex・44px・角丸スケールは ECO-009 で確立済)。画像タブ固有の配色(FS=中立グレー軸 / view=琥珀軸)は既存トークンの組み合わせで表現可能。
- **新規 surface 部品(new 6点)** は E-DESIGN-028 の consumers に画像タブ surface(E-UI-GRID-022 / E-UI-TAGASSIGN-029)を通じて接続される(既に graph_edges.consumers に両者は記載済)。
- K-DESIGN 変更なし。本台帳は E-BOM 改訂 ECO のスコープ表として使う。
