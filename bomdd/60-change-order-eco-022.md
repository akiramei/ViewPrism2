# ECO-022: 画像ビューアー タグ制御モード(フェーズ2・仕様§5)

- **status**: 設計完了・凍結待ち(G0 起票+裁定 / G1 REQ / G2 spec §2.12 / G2' 独立監査通過(3巡)/ G3 設計BOM+ドライラン PASS / 固定オラクル S-32〜S-37 作成済)。**次=maintainer レビュー→一括コミット→loop-eco022-r1 タグ付与で S-32〜S-37 凍結→隔離工場製造**。製造・受入・golden は凍結後
- **type**: 新規 Core 計算核(配置エンジン)+ 永続化拡張 + 新 surface(設定ドロワー トグル+マッピングモーダル)
- **baseline**: ビューアーフェーズ1 適用後(commit `570fdef`・main・作業ツリークリーン)
- **bom_rev**: v4.0(eco:ECO-022)
- **spec_input**: `view-prism/docs/view-prism-viewer-spec.md` §5(タグ制御モード)
- **cad_input**: `Downloads/ViewPrism2 ビューア (standalone).html`(タグ制御マッピングモーダル部分・ViewPrismUI へ未取込=要 maintainer 取込)
- **reverse_input(設計者側)**: `view-prism/src/components/ImageViewer/utils/tagControlUtils.ts`(原典の applyTagActions・getTagActionsForImage)

## 0. 裁定記録(G0・2026-06-30 maintainer)

| # | 裁定項目 | 決定 |
|---|---|---|
| TC-1 | **スコープ** | 仕様§5.5 準拠 = **見開き(右/左)はフル対応**(6アクション)、**スクロール/ノーマルは `skip` のみ**(他アクションは無効=無視)。「その他モード」はタグ制御無効。 |
| TC-2 | **紐付けキー** | **タグID**(ViewPrism2 のタグ実体)。`action → tag_id`(nullable)を永続化。原典仕様の例(タグ名)は ViewPrism2 では ID へ写像。 |
| TC-3 | **競合解決(1画像に複数制御タグ)** | **`skip > spread > forceLeftPage > forceRightPage > leftPageEmpty > rightPageEmpty`** の全順序で**支配アクション1つ**を決定。 |
| TC-4 | **進め方** | G0 charter 起票してフル BomDD 連鎖(charter→REQ→spec→E/M/CP-BOM→固定オラクル凍結→隔離工場製造→設計者受入→golden)。これまでのループと同方式。 |
| TC-5 | **漫画見開き(2枚=1見開き絵)の対応範囲** | **見開きモード限定**。①ワイド1枚スキャン=`spread` で占有(全モードで1枚として既に表示可)。②左右2枚スキャン=見開きモードで**空白ページ開始 ON(漫画標準)または右半 `forceRightPage`** で同見開きに揃える。**ノーマル/スクロールでの「2枚を1見開きに合成」表示はスコープ外**(§5.5 と一貫・現状仕様のまま)。maintainer 2026-06-30: cross-mode 合成は技術的に可能だがアイディアベースで実需要なし。採るなら別 ECO(新データモデル+2モード見開き描画)。 |

### 0.0 動機シナリオ(maintainer 2026-06-30・設計の駆動原理)

**雑誌スキャンの広告ページ除外。** 雑誌をスキャンすると広告ページが混在する。ユーザーは**広告ページを表示から除外**したい。しかし閲覧体験として、**物理的に右側にあったページは右側に表示**してほしい(左右の見開きパリティを保つ)。素朴に広告を取り除くと以後のページが半ページずれて左右が入れ替わる。そこで**ダミーページ画像をスキャンに手挿入することを避け**、タグでパリティを制御できるようにしたのがタグ制御モード。

→ この意図が6アクションの**目的**を定める(任意配置でなく**パリティ保存+制御された空白**):
- `skip` = 広告など不要ページを列から除外。
- `forceLeftPage`/`forceRightPage` = あるページを物理的な左/右へ**ピン留め**。そのために必要なら手前に空白を挿入し、除外で生じたパリティずれを是正する(=エンジンが空白を自動配置=ダミー手挿入の回避)。**主用途=開始アンカー(maintainer 2026-06-30)**: 先頭に無視したい広告がある場合、先頭の実ページ1枚の開始側を force で固定すれば(その見開きの反対側が空白になり先頭が1枚で立つ)、**残りの画像は無タグのまま正しくペアリングされる**。1タグでストリーム全体のパリティを決める「解きやすい問題」への対応。`startWithEmptyPage`(全体設定)のより狙い撃ち版に相当。
- `leftPageEmpty`/`rightPageEmpty` = その画像の見開きの左/右を**明示的に空白**にする(広告のあった面を空けて facing 実ページを物理側に残す等)。
- `spread` = 実際の見開き 1 枚画像で左右 2 ページ分を占有。

**正準 worked example/golden/オラクル基底は「雑誌の広告除去で右ページを右に保つ」シナリオとする**(G2 で逐条化)。

### 0.1 原典との競合順 divergence(意図的・記録)

原典 `applyTagActions`(tagControlUtils.ts)の評価順は **`spread`(early return)→ `force`(左右)→ `empty`(左右)→ `skip`(最後・side 単位 null + `needMoreImages`)** で、候補ペアの左右画像を**反応的に補正**し skip は呼び出し側ループで詰める方式。

ECO-022 は TC-3 で **`skip` を最上位**に置く(maintainer 裁定=私の推奨)。理由:
- §5.5 の `skip` 定義「表示対象から除外」は**列からの事前除去**が最も忠実で、純粋計算核(Core 非依存・固定オラクル化)に落としやすい。
- 原典の「リアクティブ補正+呼び出し側ループ」は副作用的で、決定的オラクルにしづらい。
- ViewPrism2 の配置エンジンは **(画像列, 解決アクション列)→ 仮想ページ列構築 → 見開き解決** の純粋関数とする(§2 アーキテクチャ)。

## 1. スコープ(TC-1)

| モード | タグ制御 | 本 ECO の挙動 |
|---|---|---|
| 見開き(右開き) | ◎ フル | 6アクション全適用(配置エンジン) |
| 見開き(左開き) | ◎ フル | 6アクション全適用(direction で左右読み順反転) |
| スクロール | △ | `skip` のみ(列から除外)。他5アクションは無視 |
| ノーマル | △ | `skip` のみ(列から除外)。他5アクションは無視 |

**有効/無効**は設定の `enableTagControl` トグル(既定 OFF)。OFF 時は全モードがフェーズ1 と完全同一(回帰ゼロの保証点)。

## 2. アーキテクチャ(Core 配置エンジン)

現 [SpreadPairCalculator](../src/ViewPrism2.Core/Services/Viewer/SpreadPairCalculator.cs) は「現在 index と index+1 を組む」**index ベースの単純ペアリング**で、タグ制御では成立しない(skip/spread/empty でページ列が画像 index と 1:1 でなくなる)。新しい純粋計算核を導入:

- **`TagActionResolver`(Core・純粋)**: 入力=画像のタグID集合 + マッピング(`action→tag_id?`)。出力=支配アクション(`ViewerTagAction?`・TC-3 の全順序で1つ)。`ViewerTagAction` enum = `ForceLeftPage/ForceRightPage/Spread/Skip/LeftPageEmpty/RightPageEmpty`。
- **`TagControlLayoutCalculator`(Core・純粋)**: 入力=順序付き (画像, 解決アクション) 列 + `SpreadDirection` + `startWithEmptyPage`。出力=**ページプラン**(`IReadOnlyList<SpreadPair>`= 見開きの並び。各 `SpreadPair` は左右の画像 index か空白 null)。`skip` は列から除去、`spread` は単独占有の見開き、`leftPageEmpty/rightPageEmpty` は隣接空白挿入、`forceLeftPage/forceRightPage` はスロット整列(必要なら空白挿入)。
- ナビゲーション(送り/位置)は**ページプランの spread index** で動く。タグ制御 ON 時の `PageTurnCalculator` 相当は plan 長基準(plan を跨ぐ skip/spread を吸収済み)。

配置アルゴリズムの逐条定義(force=スロット整列 vs empty=隣接空白挿入 の厳密意味論・端処理・空白開始との合成)は G2 spec §2.12 で worked example + 固定オラクル期待ベクタとして確定済み。G0 はアーキテクチャと責務分離(Core 純粋・ViewModel は plan 消費)を確定し、G2/G3 で製造可能な契約へ展開した。

既存核(SpreadPairCalculator/PageTurnCalculator/ScrollPositionTracker/位置記憶)は**タグ制御 OFF 経路で不変**=回帰ゼロ。ON 時は配置エンジンの plan に切り替える。

## 3. データ配管(既存活用=新規最小)

**`ImageEntry` は既に `IReadOnlyList<EvalTagValue> Tags` を保持**(`EvalTagValue(string TagId, TagType, string? Value)`)。ビューアは `ShowViewer(IReadOnlyList<ImageEntry> ordered, ...)` で画像ごとのタグID を**既に受領済み**。よって:
- タグ→アクション解決に必要な入力(画像のタグID集合)は**追加配管なしで取得可能**。
- 必要な新規入力は **(a) マッピング(action→tag_id?)** と **(b) enableTagControl** のみ=設定から供給。
- 作成済みタグ一覧(マッピングモーダルの picker 候補・色ドット)は別途 `ITagRepository`/`TagService` を WindowService 経由で供給(ビューアは App 層 surface なので注入可)。

## 4. 永続化設計(フェーズ1 ViewerSettingsModel 拡張パターン踏襲)

- `AppSettings` に追加: `ViewerEnableTagControl`(bool)+ `ViewerTagMap_<Action>`(6個・tag_id 文字列 or 空)。`TolerantStringConverter`/`NormalizeViewerSettings` でフェーズ1 同様の**破損耐性**(CP-SET-009 同型)。
- `ViewerSettingsModel` に `EnableTagControl` + `TagActionMap`(`Dictionary<ViewerTagAction, string?>` 相当)を追加。`Parse/ToString` round-trip + 既定 + 前方互換。
- 永続化は即時(REQ-059 同様 `_persist`)。マッピング変更・トグル切替で settings.json へ即保存。
- **削除タグの扱い**: マッピング先 tag_id が現存タグに無い場合は「未割り当て」相当(解決時に無視)。破損耐性の一部として G2 で明文化。

## 5. マッピングモーダル(CAD 由来=`タグ制御マッピング`)

モック構造(Downloads モックから抽出):
- 設定ドロワーに **「タグ制御モード」トグル**(enableTagControl・有効/無効)+ マッピングを開く導線。
- **モーダル「タグ制御マッピング」**(ビューア内中央オーバーレイ=ECO-019 in-tab popup と同型): ヘッダ(紫アイコン+説明「予約アクションに、あなたが作成したタグを割り当てます…」)+ 3カラムグリッド `[グリフ30px][予約アクション(名前+en monospace+説明)1fr][割り当てるタグ picker 248px]` × **6行(予約アクション)**。
- **タグ picker**: 割当タグ(色ドット+ラベル)or「未割り当て」+ chevron。メニューで作成済みタグ一覧(色ドット)+ クリア(未割り当て)。
- DS は ECO-019/整理トレイの in-tab popup 部品系を再利用見込み(新規 DS は最小化)。視覚は golden で確定。

## 6. BOM 連鎖計画(進捗反映)

| ゲート | 状態 | 成果物 |
|---|---|---|
| G1 要件分解 | **完了** | `10-requirements.yaml` に REQ-076(配置エンジン)/ REQ-077(マッピング永続化)/ REQ-078(マッピングモーダル+トグル surface・表示契約込み)。 |
| G2 仕様 | **完了** | `20-spec.md` §2.12 = 配置エンジン逐条 + worked example 6本 + 競合順 + 有効モードマトリクス + 永続化 + OC-23/24/25 + golden G-11。 |
| G2' 独立監査 | **通過** | 3巡(major3→補正→新blocker[両面空白]→seed 抑止補正→クリーン)。blocker0/major0/minor0。 |
| G3 設計 | **完了** | `30-ebom`(E-VIEWER-TAGCTRL-044+E-UI-VIEWER/E-SETTINGS 拡張)/ `32-mbom`(M-TAGCTRL-028+M-UI-018+FMEA-035/036+ずる列挙)/ `33-control-plan`(CP-TAGCTRL-024+CP-SET-009 拡張+CP-UI-G11)/ `34-routing`(routing_eco022)。K-BOM 既存 K-DESIGN/K-AVALONIA で充足。 |
| G3 ドライラン | **PASS** | 隔離工場が製造パッケージのみで着手可・質問ゼロ・blocker0・実コード前提(a)〜(e)実在。凍結前補正2件適用。 |
| 固定オラクル | **作成済(凍結待ち)** | `41-fixed-oracle` に S-32〜S-37(配置エンジンの決定的期待ベクタ・全 cross-factory)。**maintainer レビュー→一括コミット→`loop-eco022-r1` タグ付与で凍結**。 |
| 製造 | 凍結後 | fresh 隔離工場(原典・固定オラクル非開示)で製造。 |
| 受入 | 製造後 | 設計者が S-32〜S-37 を Oracle へ実装し全 PASS + 既存 S-01〜S-31 回帰ゼロ。 |
| golden | 受入後 | maintainer 実機(見開きで skip/spread/force/empty が効く・マッピング保存/復元・モーダル表示パリティ G-11)。 |

## 7. 固定オラクル計画(配置エンジン=決定的・純粋)

- `TagActionResolver`: 競合順(TC-3 全順序)・未マッピングタグ無視・削除タグ無視。
- `TagControlLayoutCalculator`: 6アクション各々の配置・複合・端処理・空白開始合成・右開き/左開きの読み順。
- 期待値は spec §2.12.3 worked example から逐条導出(this-build 依存なし=全 cross-factory)。**作成済=S-32〜S-37(凍結待ち)**。

## 8. スコープ外
- スクロール/ノーマルでの skip 以外のアクション(TC-1)。
- 原典の「次の適切な画像を探す」非決定的ループ(配置エンジンの決定的版で代替)。
- タグ作成/編集(タグタブの責務)。マッピングモーダルは既存タグの割当のみ。
- ビューア以外のタグ意味論変更(なし=読み取りのみ)。
