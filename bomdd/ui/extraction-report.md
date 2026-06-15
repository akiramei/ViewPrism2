# UI-IR / UI-BOM 抽出レポート — ViewPrism2 タグタブ

> ステータス: candidate / 未昇格。BomDD `method/ui-ir-ui-bom.md` 準拠。
> 生成日: 2026-06-16

## 入力概要

- 画面名: タグタブ(タグ管理画面・3ペイン)
- 入力 HTML:
  - M1 `work/tag-tab/ViewPrism2 タグ管理.dc.html`(3ペイン全体)
  - M2 `work/tag-tab/ViewPrism2 タグ作成ダイアログ.dc.html`(適応型モーダル)
  - M3 `work/tag-tab/ViewPrism2 配置タグ設定.dc.html`(階層フル版+条件ダイアログ)
- JavaScript 有無: 有(各 `.dc.html` 内 `<script type="text/x-dc">` の DCLogic、共通 `support.js`)
- CSS 有無: 有(インラインスタイル中心+helmet `<style>`)
- 機能説明: 有(S `work/tag-tab/ViewPrism2 タグタブ仕様書.dc.html`)
- 既存 UI-IR / UI-BOM: なし(本抽出が初回)
- 既存 `data-ui-id` / `data-ui-temp-part-no`: **なし**(全モックに不在 → stable id・仮品番を新規付与)

### 重要な構造判断
3つの画面モックは実体としては **1画面=タグタブ**。M1=3ペイン全体、M3=M1中央『階層構造』の **高忠実度版**、M2 と条件ダイアログはこの画面上の **モーダル**。したがって `screen` は1つ(`TMP-UI-SCR-0001`)とし、M2/条件ダイアログは component(modal)として扱った。

## 抽出した画面

| uiId | 仮品番 | 名称 | 信頼度 |
|---|---|---|---|
| screen.tag-management | TMP-UI-SCR-0001 | タグタブ画面 | high |

## 抽出した領域

| uiId | 仮品番 | 名称 | 役割 | 信頼度 |
|---|---|---|---|---|
| region.shell-nav | TMP-UI-REG-0001 | シェルナビ | タブ切替+検索+設定(**シェル所有・tag タブ外**) | high |
| region.view-management | TMP-UI-REG-0002 | ビュー管理(左) | ビュー一覧/作成/選択 | high |
| region.hierarchy | TMP-UI-REG-0003 | 階層構造(中央・主役) | タグ入れ子ツリー・配置編集 | high |
| region.tag-palette | TMP-UI-REG-0004 | タグパレット(右) | 定義済タグ一覧/検索/作成入口 | high |

## 抽出した UI 部品候補

| 仮品番 | 名称 | 種別 | 出現 | BOM 対象理由 |
|---|---|---|---|---|
| TMP-UI-CMP-0001 | ビュー行 | component | (データ駆動) | 再利用行・表示契約(DC-VIEWLIST-001)対象 |
| TMP-UI-CMP-0002 | 階層行 / 配置タグ行 | component | group/leaf(OCC-0004/0005) | エディタ中心部品・別名/条件/ホームの宿主。M1/M3 統合 |
| TMP-UI-CMP-0003 | パレットタグカード | component | simple/text/number(OCC-0001/0002/0003) | 種別分岐の再利用カード・D&D 発生源 |
| TMP-UI-CMP-0004 | 種別チップ | component(atom) | — | 全画面再利用・色トークン依存 |
| TMP-UI-CMP-0005 | カラードット | component(atom) | — | タグ識別色の再利用アトム |
| TMP-UI-CMP-0006 | 条件サマリチップ | component | — | 条件状態可視化・i18n 整形契約(GF-05) |
| TMP-UI-CMP-0007 | タグ作成/編集ダイアログ | component(modal) | — | タグ定義の主要 surface・適応型 |
| TMP-UI-CMP-0008 | 条件設定ダイアログ | component(modal) | — | 配置タグの条件 surface |
| TMP-UI-CMP-0009 | 種別セグメント | component | — | 適応型分岐源 |
| TMP-UI-CMP-0010 | カラーパレット | component | — | トークン選択 |
| TMP-UI-CMP-0011 | 選択肢行 | component | — | 候補値編集 |
| TMP-UI-CMP-0012 | 付与時プレビュー | component | — | 付与表現の整合(E-UI-TAGASSIGN-029 read-across) |
| TMP-UI-CMP-0013 | 条件タイプセグメント | component | — | 演算子集合の選択 |
| TMP-UI-CMP-0014 | 候補値チェックリスト | component | — | select 条件・空状態あり |

部品マスターと出現の分離:`TagCard`(CMP-0003)= マスター、simple/text/number = 出現(OCC-0001〜0003)。`HierarchyRow`(CMP-0002)= マスター、親(group)/子(leaf)= 出現(OCC-0004/0005)。

## 抽出した操作(主要)

| 仮品番 | 名称 | トリガー | 想定効果 | 仕様リンク要否 |
|---|---|---|---|---|
| TMP-UI-ACT-0001 | 新規ビュー | click | 空ビュー作成 | 要(REQ-030) |
| TMP-UI-ACT-0002 | ビュー選択 | click | 階層を中央へ表示 | 要(REQ-030/034) |
| TMP-UI-ACT-0004 | 入れ子・並べ替え | drag | 親子/並び変更 | 要(REQ-034/035) |
| TMP-UI-ACT-0006/7/8 | タグ作成/編集/削除 | click | タグ CRUD | 要(REQ-020/024/025) |
| TMP-UI-ACT-0009 | パレット→階層へ配置 | drag | 配置生成 | 要(REQ-034) |
| TMP-UI-ACT-0010 | 種別切替 | click | 適応切替 | 要(REQ-020) |
| TMP-UI-ACT-0016 | タグ保存 | click | 永続化+検証 | 要(REQ-020/022) |
| TMP-UI-ACT-0018 | ホーム設定 | click(⬡) | ホーム解決 | 要(REQ-037) |
| TMP-UI-ACT-0019/20 | 別名編集/確定 | click/Enter | 配置別名 | 要(REQ-036) |
| TMP-UI-ACT-0021 | 条件ダイアログ起動 | click(じょうご) | 条件入口 | 要(REQ-036) |
| TMP-UI-ACT-0024 | 条件タイプ切替 | click | 演算子選択 | 要(REQ-031) |
| TMP-UI-ACT-0027 | 条件適用 | click | 条件永続化+検証 | 要(REQ-031/036) |

(全30操作は `ui-ir.json` / `ui-bom.json` 参照)

## 抽出した入力項目(主要)

| 仮品番 | 名称 | 型 | データ概念 | バリデーション |
|---|---|---|---|---|
| TMP-UI-INP-0002 | タグ名 | text | Tag | 必須(空で保存不可) |
| TMP-UI-INP-0004 | カラー | enum(swatch) | Color | 必須 |
| TMP-UI-INP-0006/7/8 | 最小/最大/ステップ | numeric | NumericRange | 最小≦最大、ステップ>0 |
| TMP-UI-INP-0010 | 別名 | text | Alias | 空で原名復帰 |
| TMP-UI-INP-0011 | パターン(regex) | text | Condition | 不正 regex は不成立(E-EVAL-002) |
| TMP-UI-INP-0012 | 候補値選択 | multiselect | Condition | 1つ以上必須 |
| TMP-UI-INP-0014/15 | 範囲 min/max | numeric | Condition | 両端含む |

## 抽出した状態(主要)

| 仮品番 | 名称 | 対象 | 観測方法 |
|---|---|---|---|
| TMP-UI-STA-0004 | 種別適応 | 作成ダイアログ | 右側+プレビュー連動 |
| TMP-UI-STA-0005 | 保存不可 | 保存ボタン | 淡色・非活性(名前空/候補0) |
| TMP-UI-STA-0006 | 選択肢0空状態 | 作成ダイアログ | 破線プレースホルダ |
| TMP-UI-STA-0007 | ★/数値モード | プレビュー | ★並び or 数値 |
| TMP-UI-STA-0008 | ホーム指定中 | 配置タグ行 | ⬡塗り+HOMEピル+行背景 |
| TMP-UI-STA-0009 | 別名あり | 配置タグ行 | 別名＋『＝元名』 |
| TMP-UI-STA-0011 | 条件あり | 条件バッジ | サマリチップ |
| TMP-UI-STA-0012 | 条件タイプ分岐 | 条件ダイアログ | 入力欄切替 |
| TMP-UI-STA-0013 | 適用不可 | 適用ボタン | 淡色(検証) |
| TMP-UI-STA-0014 | 候補値なし空状態 | 候補リスト | パターン案内 |

## BOM 対象外にした要素

| selector(locator) | 理由 | handling |
|---|---|---|
| ロゴ三角・グラデバンド・原則カード・トークンデモ | プレゼン装飾(マーケ) | nonBom(トークンデモのみ E-DESIGN-028 reference) |
| 『NEXT STEPS / NEXT』フッタ | ドキュメント注記 | nonBom |
| M2 ghost backdrop + scrim | モーダル提示演出 | nonBom |
| 余白 wrapper・罫線/影/角丸のみ div | レイアウト/装飾 | nonBom |
| タイトルバー window controls・メニューストリップ | シェル装飾・tag タブ外 | trace(E-UI-SHELL-021) |
| S(仕様書).dc.html 全体 | 機能仕様ドキュメント(対象アプリ画面ではない) | nonBom(本文は抽出根拠) |

## E-BOM 連携候補

タグタブは既存 E-BOM に対応 surface が既に存在するため、本 UI-BOM は **新規創出ではなく既存 E-BOM 品目への接続(read-across)** が主。

| UI-BOM 群 | 接続先 E-BOM(候補) | 想定 core / Control Plan / K-BOM |
|---|---|---|
| ビュー管理・パレット・タグカード・作成ダイアログ・色選択・選択肢 | **E-UI-TAGS-026** | core: E-TAGSVC-008 / E-VIEWSVC-009 · CP: CP-UI-G6, CP-TAG-011, CP-DISPLAY-PARITY-022 · K: K-AVALONIA/K-MVVM/K-DESIGN |
| 階層構造・配置タグ行・別名・ホーム・条件バッジ・条件ダイアログ・タグ配置 | **E-UI-NODEGRAPH-025** | core: E-GRAPH-003 / E-VIEWSVC-009 / E-EVAL-002 · CP: CP-UI-G6, CP-GRAPH-002, CP-EVAL-001, CP-GF-015 |
| 条件タイプ(pattern/select/equal/range)・各入力 | **E-EVAL-002**(核) | CP: CP-EVAL-001 · K: K-REGEX |
| 種別チップ・カラードット・カラーパレット・行44px | **E-DESIGN-028** | K: K-DESIGN |
| 条件サマリ整形・全テキスト | **E-I18N-ASSETS-027** / E-I18N-014 | CP: CP-GF-015, CP-UI-G5 |
| 全ドメイン概念(Tag/View/Hierarchy/Placement/Condition/Home…) | **E-DOMAIN-001** | 各サービス核 |
| シェルナビ・⌘K | **E-UI-SHELL-021** | — |
| 付与時プレビュー(★/数値±/候補チップ) | E-UI-TAGS-026 ↔ **E-UI-TAGASSIGN-029**(read-across) | 付与表現の整合(UQ) |

### 既存 E-BOM とのギャップ → 決定(2026-06-16 解決)

read-across で6件のギャップを検出し、UQ-E1〜E5 を確定した(`unresolved-questions.md` 参照)。**3件は既存 E-BOM の改訂(ECO)案件**となり、**ECO-007** として一括起票した(`../60-change-order-eco-007.md`)。

| # | 項目 | 決定 | E-BOM 影響 |
|---|---|---|---|
| 1 | ビュー行(UQ-E1) | お気に入り★削除・名前+タグ数・説明 tooltip | **ECO**: E-UI-TAGS-026 `DC-VIEWLIST-001` 改訂(ECO-004 是正の差し戻し) |
| 2 | タグカード(UQ-E2) | 説明欄は不要 | **ECO**: E-UI-TAGS-026 `DC-TAGPALETTE-001` 改訂(ECO-004 是正の差し戻し) |
| 3 | タグを配置ボタン(UQ-E3) | モック側(汎用ラベル)採用 | **ECO**: E-UI-NODEGRAPH-025 `GF-04` 撤回(v2.0 是正の差し戻し) |
| 4 | ⌘K グローバル検索(UQ-E4) | 無し(不採用) | なし(現行 E-BOM 未記載) |
| 5 | 付与プレビュー(UQ-E5) | 表示一致を契約化 | **新規契約**: `DC-TAGPREVIEW-001`(E-UI-TAGS-026 ↔ E-UI-TAGASSIGN-029) |
| 6 | ホーム表現 | 家アイコン⬡採用 | 既存 GF-03(★不可)に整合 ✓(変更なし) |

> 1/2/3 は過去の意図的是正(1・2=ECO-004 read-across、3=v2.0 GF-04)の差し戻しに相当。ECO 起票時に撤回根拠を明記する。

## 抽出時の仮定

| ID | 内容 | 信頼度 |
|---|---|---|
| ASM-001 | M3 の階層は M1 中央ペインと同一 region(高忠実版)である | high(同一ヘッダ・同一ビュー『ロケーション』・同一コンテナ) |
| ASM-002 | 作成ダイアログは パレット「追加」/カード「えんぴつ」から起動する | high(仕様 §05 で明示) |
| ASM-003 | D&D(配置/並べ替え/選択肢順)は静的モックだが仕様テキストで意図確認済 | medium(モック未実装) |
| ASM-004 | 個々のデータ行(地域・季節等)は出現ではなく**データインスタンス**として扱い、出現は variant 粒度(simple/text/number, group/leaf)で記録 | medium(方法論の例示は per-instance だが BOM 爆発回避のため variant 粒度を採用 → UQ-M1) |
| ASM-005 | パレット検索は部分一致(実装 `includes()`)。前方一致/対象は要確認 | medium |

## 成果物

| ファイル | 役割 |
|---|---|
| `ui-ir.json` | 観測・追跡用 中間表現(全要素) |
| `ui-bom.json` | BOM 昇格候補(E-BOM 連携付き) |
| `ui-trace-map.json` | HTML locator / UI-IR / UI-BOM / E-BOM の対応 |
| `extraction-report.md` | 本書 |
| `unresolved-questions.md` | 昇格前の確認事項 |
