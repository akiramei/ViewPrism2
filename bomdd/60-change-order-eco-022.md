# ECO-022: 画像ビューアー タグ制御モード(フェーズ2・仕様§5)

- **status**: 製造済・設計者受入 PASS・**golden G-11 第1回=maintainer 実機で実質パス(2026-07-01・GF-TAGCTRL-01〜04 是正済・§9)**(G0〜G3 完了 / G2' 通過 / 固定オラクル S-32〜S-37 凍結=loop-eco022-r1=ce53d46 / 製造=**工程逸脱あり**[工場死亡・cheat-log/castability 欠落・出所不確実] / 設計者受入=S-32〜S-37 26ケース全PASS+回帰ゼロ+surface ずる査読 blocker0)。**逸脱は register.deviation / routing.manufacturing_acceptance に記録(ECO-003 同型)**。**残=maintainer 一括コミット時に 50-as-built へ CP-UI-G11 approved 記録→クローズ**。検証: Oracle 100 PASS+2skip / Tests 505 PASS / build 0警告
- **type**: 新規 Core 計算核(配置エンジン)+ 永続化拡張 + 新 surface(設定ドロワー トグル+マッピングモーダル)
- **baseline**: ビューアーフェーズ1 適用後(commit `570fdef`・main・作業ツリークリーン)
- **bom_rev**: v4.0(eco:ECO-022)
- **spec_input**: `view-prism/docs/view-prism-viewer-spec.md` §5(タグ制御モード)
- **cad_input**: `ViewPrismUI/資料/ビューア/ViewPrism2 ビューア (standalone).html`(タグ制御マッピングモーダル部分)。**2026-07-01 に ViewPrismUI へ取込済**: 一次モックを `資料/ビューア/` へコピー + UI-IR `docs/screens/image_viewer_tag_control.md`(モック権威)作成。突合結果は §9 GF-TAGCTRL-05 参照。当初「未取込のまま製造」が §10 工程欠陥の主因。
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

## 9. golden G-11 第1回(2026-07-01・maintainer 実機)= GF 是正して受入

**結果**: #1 設定ドロワースクロール OK / #2 マッピングモーダル(モック準拠)OK / #3 配置パリティ OK。
end-to-end 確認: タグ タブでタグ追加 → 画像タブでタグ編集候補に表示 → ビューアーで タグ×アクション マッピング →
**左固定(forceLeftPage)・スキップ(skip)が実配置に効くのを確認**。台帳再読込修正で再起動不要も確認。

**GF(golden 所見)是正**(いずれも「憶測修正の反復禁止」=ヘッダレス実測 or maintainer 実機の ground truth で確認してから是正):

| GF | 症状 | 真因 | 是正 |
|---|---|---|---|
| GF-TAGCTRL-01 | 設定ドロワーがスクロールせずタグ制御カードが窓下で切れ到達不能(製造時からの潜在バグ・タグ制御追加で顕在化) | ScrollViewer が非有界高さを受領(ドロワーが非有界コンテナ下) | drawer Border を Grid の `*` 行(`Grid.Row=1`)へ直接置き有界化。ScrollViewer.Padding を内容 StackPanel の Margin へ移動(Avalonia の ScrollViewer.Padding は Extent 非算入で下端切れ)。ヘッドレス実測: Viewport 646 < Extent 733=スクロール可・下端到達 |
| GF-TAGCTRL-02 | マッピングモーダルがモック非準拠で下部(フッター)欠け | フッター(完了/既定に戻す)不在+実機フォントで最終行がモーダル下端に迫る | DockPanel でヘッダ(タグアイコンバッジ+タイトル+説明+X)/行カード(アクション固定色アイコンバッジ・モック順)/常時 docked フッター(補足+既定に戻す+完了 cta)へリワーク。即時適用ライブ反映維持・完了=閉じる・既定に戻す=全アクション未割り当て(新 `ResetTagControlMappingCommand`)。行文言は凍結挙動に忠実な現行を維持 |
| GF-TAGCTRL-03 | フッター補足文が右端でハードクリップ | 横 StackPanel でテキストに無限幅が渡り折り返し不能 | icon(Auto)+text(`*`)の Grid へ改め折り返し可能化+文言 1 行短縮(ja「未割り当ては無効。1タグを複数アクションへ割当可。」)。実測: 希望 280px < 列 319px で 1 行収まり |
| GF-TAGCTRL-04(隣接) | 新画像タブ(M3 surface)でタグ タブ新規タグがタグ編集候補に出ない(maintainer 仮説「ビュー階層フィルタ」は誤り) | M3 `ImageTabViewModel._tagById` は起動時 1 回のみロード。`_imagesTabStale`→`ReloadAsync()` は legacy surface のみ再読込で M3 台帳未更新 | 公開 `ReloadTagCatalogAsync()`(タグ台帳+ビュー+画像タグ紐付け再読込・コレクション/ナビ/選択/表示モード保持)を追加しタブ切替 stale 経路で併呼。再起動不要で新規タグ反映 |

**検証手段の教訓**: dev exe を遠隔描画確認できないため、視覚レイアウトは scratch の Avalonia.Headless プローブ(App リソース込み起動 → 実 Window を測定)で Extent/Viewport を実測してから是正した(憶測修正の反復を回避)。Loc 未注入だとテキスト空で高さが非現実的になる点も是正済み。

### GF-TAGCTRL-05(追随・実装済 2026-07-01): golden 承認版がモック(CAD)の簡略サブセット

工程 retro(§10)の B(ViewPrismUI へ IR 取込)実施中、standalone.html モック × golden 実装を突合した結果、**G-11 承認版はモックの簡略サブセット**と判明(maintainer 2026-07-01・裁定=IR はモック権威・実装は追随 GF で寄せる)。IR は `ViewPrismUI/docs/screens/image_viewer_tag_control.md`(モック権威)。乖離 9 点:

| # | 項目 | モック(正) | golden 実装(現状) | 裁定 |
|---|---|---|---|---|
| D1 | モーダル幅 | 820px | 600px | モックへ是正 |
| D2 | 列見出し行(予約アクション/割り当てるタグ) | 有り | 無し | モックへ是正 |
| D3 | picker 幅 | 248px | 180px | モックへ是正 |
| D4 | picker「使用中」バッジ(他アクション割当済タグの可視化) | 有り | 無し | モックへ是正(VM 対応要=他アクション使用中判定) |
| D5 | picker 選択中✓ | 有り | 無し | モックへ是正 |
| D6 | フッター補足文 | 全文 | 短縮(GF-03) | モックへ是正(820px なら全文 1 行=短縮不要。GF-03 は 600px 独自狭小の副作用) |
| D7 | アクショングリフ | ◧ ◨ ▭ ⊘ ▯ ▯ | ◀ ▶ ↔ ⊘ ◑ ◐ | モックへ是正 |
| D8 | 行の名前/説明 | 短い(左空白挿入/位置を補正) | 現行 i18n(長め) | **許容差分**(現行は凍結挙動に忠実・maintainer 既決) |
| D9 | ヘッダ紫 | #8b5cf6 | #7C3AED | モックへ是正(些細) |

#### ビューア本体の乖離 V1〜V5(CAPA 横展開監査で検出・`ViewPrismUI/docs/screens/image_viewer.md`)

CAPA UI-IR 横展開監査(2026-07-01)で画像ビューアー本体を新設 IR(`image_viewer.md`)へモック権威で取込み、golden 実装との乖離 V1〜V5 を検出。**V2/V3/V4 の裁定は maintainer が 2026-07-01 に確定**(V5 は当初から許容差分):

| # | 項目 | モック(正) | golden 実装(現状) | 裁定(maintainer 2026-07-01) |
|---|---|---|---|---|
| V1 | 設定ドロワー幅 | 376px | 360px | モックへ是正(軽微) |
| V2 | ドロワーのスクリム | 有り(暗転+スライドオーバー=モーダル的) | 無し(`*` 行の右パネル) | **現行維持=許容差分**。理由=設定変更をキャンバスで即プレビューしたい/有界スクロール(GF-TAGCTRL)・Z オーダー(GF-V2-TC-02)の既存 golden を崩さない |
| V3 | ドロワー内ヘッダ | 有り(歯車+表示設定+モードバッジ+X) | 無し(表示設定はツールバー toggle) | **モードバッジのみ追随**(現在モードの可視化だけ拾う・閉じる X は付けない=V2 現行維持ゆえトグルで閉じられ X は冗長) |
| V4 | ツールバー「タグ制御 ON」バッジ | 有り(`N/6` 付き) | 無し | **追随**(有用な状態可視化・VM に `MappedActionCount`/`TagControlMappingBadge` 既存で低コスト) |
| V5 | タイトルバー | 擬似バー+窓制御 | Avalonia ネイティブ窓枠 | **許容差分**(ネイティブ窓が意図・当初から) |

**GF-05 実装スコープ(追随分)**: モーダル D1〜D7・D9 + ビューア V1(幅376)・V3(モードバッジのみ)・V4(タグ制御 ON バッジ)。**許容差分**=D8(行文言)・V2(スクリム)・V5(タイトルバー)。

#### GF-05 実装(2026-07-01・全モック実測値は standalone.html 権威で抽出)

すべての寸法・色・グリフ・文言を一次モック `資料/ビューア/ViewPrism2 ビューア (standalone).html` の DCLogic/CSS から抽出して寄せた(憶測修正を排除)。実装内訳:

- **D7 グリフ+色**(`ViewerViewModel.ActionRowDefs`): グリフ ◧◨▭⊘▯▯(mock `actions[].glyph`)。IconFg=アクション色 100%(mock `actionMeta.color` = #2F6BED/#12A594/#8B5CF6/#E5484D/#E8B931/#F2912B)、IconBg=同色 12% alpha(mock `hexA(color,0.12)` = ARGB `#1F`+RRGGBB)。
- **D4 使用中 / D5 選択✓**(`TagControlMappingRow.TagPickerOption` に `IsSelected`/`UsedElsewhere` 追加・`RebuildTagActionRows` が `usedBy` マップで行別算出): picker メニュー行に使用中バッジ(#C08A2A on #FDF2DD)と選択✓(#2F6BED)を条件表示。
- **D1/D2/D3/D6/D9**(`ViewerWindow.axaml`): モーダル幅 820・角丸 14(D1)/ 列見出し「予約アクション」「割り当てるタグ」grid 34,*,248・色 #AAB1BD(D2)/ picker 列 248・メニュー幅 248(D3)/ フッター全文へ復帰(D6・i18n)/ ヘッダ タグアイコン #8B5CF6(D9)。
- **V1/V3/V4**(`ViewerWindow.axaml` + VM): ドロワー幅 376(V1)/ ドロワー先頭にモードバッジのみ(`CurrentModeLabel`・#2F6BED on #EAF1FE・歯車/X なし・V3)/ ツールバー「タグ制御 ON」バッジ(`ShowTagControlBadge`=見開き+ON 時・紫ドット+`N/6`・#7A5BD0 on #F3EFFE・V4)。
- **i18n**: footer 全文(ja/en)+ 新規 `mapping.colAction`/`mapping.colTag`/`inUse`/`badge`(ja/en)。

**検証**: App build 0 警告 / Tests **510**(+1=新 headless probe「タグ制御マッピングモーダルは幅820で760窓に収まる」で D1 幅 820 を実レイアウト実測=change-order 残作業の headless 再計測をガード化。既存ドロワー probe は 376 へ更新)/ Oracle 100+2skip(Core 意味論不変)。**視覚細部(グリフ描画・色・バッジ)の最終確認は maintainer 実機 golden がゲート**(dev exe 遠隔描画不可)。

**golden 承認=完了(2026-07-01・maintainer 実機)**: モーダル 820・列見出し・使用中/✓・グリフ/色・フッター全文・ドロワー 376・モードバッジ・ツールバー ON バッジをすべて実機確認し **CP-UI-G11 approved**。許容差分(D8/V2/V5)も合意どおり。golden は本 change-order + `60-change-register.yaml`(golden: approved)で管理(ECO-017〜021 同様 `50-as-built.yaml` へは非追記=V4 ループ権威との切り分け)。**→ ECO-022 クローズ**。

## 10. 工程 retro(mock→UI-IR→BOM 欠陥の是正・2026-07-01 maintainer)

**問題整理(maintainer 承認)**: マッピングモーダルを今回モックから作り直したのは**製造欠陥ではなく工程欠陥**。`cad_input`(§0 冒頭)が「タグ制御マッピングモーダル部分・**ViewPrismUI へ未取込**」のまま製造へ進んだため、UI-IR が存在せず、BOM(spec §2.12.6・M-UI-018 tagctrl_ui・invariant)は**「6行×提示フィールド」の表示契約しか作れなかった**。ヘッダ/フッター(既定に戻す・完了)/カード化/行順/全体クロームは**「視覚は golden で確定」へ先送り**され、結果 **golden G-11 が照合でなく設計工程(モックからの作り直し)**になった。対照: 修復候補カード(M-UI-REPAIR-027)は表示パリティ契約 + FMEA-031 の脱漏ガードを持っていた=モーダルには**クローム脱漏 FMEA が欠けていた**。

**再発防止ガード(本セッションで bomdd/メモリへ記録・アプリ実装変更なし)**:
- **A(製造ゲート)**: `cad_input` が ViewPrismUI/UI-IR **未取込の surface は製造をブロックし IR 取込を先行**、またはやむを得ず進めるなら「試作・golden で作図」と明示スコープ化する(field 契約だけで黙って製造しない)。→ **FMEA-037**(32-mbom)に起票。
- **C(パリティ拡張)**: **CP-UI-G11**(33-control-plan)を「提示フィールド」から**クローム全体(ヘッダ/フッター/カード/行順/全体オーバーレイ)をモック突合**へ拡張。ドロワースクロール有界(GF-01)・フッター非クリップ(GF-03)も明記。
- 教訓を メモリ `mock-ui-ir-is-cad`(Why(2))へ保存。

**B(残タスク・未着手=maintainer 実施)**: モーダルを **ViewPrismUI へ UI-IR として取込**。ただし**「今の実装をそのまま CAD 原器化」しない**。`Downloads/ViewPrism2 ビューア (standalone).html` のモック × 今回の golden 確定結果を**突合したうえで、正しい CAD/IR として取り込む**(maintainer 2026-07-01)。取込後は以後のモーダル変更を IR 由来に統一。
