# Change Order — ECO-084(staged): 階層ビューの表示モード切替「すべて/未分類」(累積フィルタ+最深配置)

- 起票日: 2026-07-14 / 報告者: maintainer
- 種別: 新機能(ビュー軸ブラウズの表示意味論拡張)
- baseline: main `cfdf071`

## 1. 要求(maintainer・2026-07-14)

現行のビュー軸ブラウズは、ノード選択=パス条件の累積 AND フィルタである(REQ-036)。
ルートにはコレクションの全画像が存在し、階層のタグを選ぶほど絞り込まれる。子孫リーフに
マッチする画像は祖先ノードにも表示される(条件が部分集合関係のため)。

これに加え、**各ノードで「直下の子ノードのいずれにもマッチしない画像だけ」を表示する**
モード(最深配置=エクスプローラーのフォルダ的な意味論)を追加し、両モードを切替可能に
したい。要求の動機は「リーフでマッチする画像はノードに表示しない動き」= 各画像が最深の
マッチ位置にだけ現れるブラウズ体験と、未分類画像の発見。

### maintainer 裁定済み事項(2026-07-14・起票前の相談で確定)

| # | 裁定 |
|---|---|
| 1 | **方式=案B改**: 画像タブ(ビュー軸選択時)にトグルを置く。ビュー定義(DB)には持たせない。ビュー毎の最終選択モードは settings.json(REQ-052 基盤)へ記憶。**DB マイグレーションなし・コレクションパッケージ形式(REQ-093)不変** |
| 2 | **ルートの意味論=未分類**: 最深配置モードのルートは「階層のどのトップノードにもマッチしない画像」(未分類画像の発見器) |
| 3 | **概念語=「全て/未分類」の切替**。表示文言はツールにふさわしいものを設計時に確定(ja/en 両方=ECO-079 教訓) |

## 2. 工程診断 — 新機能: CAD 未定義が正・CAD 先行(gate①)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **未定義(新機能なので当然)** — 是正起点 | `docs/screens/image_tab.md` に表示モード/未分類/最深配置の言及なし(2026-07-14 grep 実測)。§表示軸/タグビュー節は累積フィルタ意味論のみ記述 |
| BOM(spec/E-BOM) | 未宣言 | 仕様 §2.4(REQ-034〜037)は累積フィルタ(REQ-036)のみ。新 REQ の追加が必要 |
| 実装 | 未実装(欠陥ではない) | `PathConditionConverter`(OC-3)+評価器(OC-1)は累積 AND を正しく実装。逸脱なし |

**結論**: 不具合ではなく新機能。工程順は CAD(mock+docs/screens 改版)→ 仕様 §2.4 追補 →
BOM 宣言 → 実装。gate① = ViewPrismUI 側のモック承認。

## 3. 切り分け済みの事実(2026-07-14 実測・コード読解で確定)

**確定(実測済み)**:

- 評価は in-memory: `ImageTabViewModel.ViewMatched`(src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:719)
  → `PathConditionConverter.BuildConditions`(OC-3)→ 評価器(OC-1)。DB 往復なし。
- 子ノードの matched 集合は親 matched の部分集合(子条件=親条件への AND 追加)。ECO-026 の
  `within` 最適化コメント(同 :713-718)に明記され、**子ノードの matched は件数表示のため既に計算されている**。
- したがって最深配置は `display(N) = matched(N) − ⋃ matched(child)`(**直下の子のみで十分** —
  子孫条件は子条件の上位集合のため、深いノードにマッチする画像は必ず中間の子にもマッチする)。
  差集合は HashSet O(n)・性能影響ほぼゼロ。
- settings.json への閲覧状態の追加は前例あり: E-SETTINGS-013 は REQ-052(ウィンドウ状態)→
  REQ-059(ビューア設定 7 項目)→ REQ-077(タグ制御設定)と追記実績があり、DB 非関与。

**意味論の副作用(仕様化必須・golden 固定対象)**:

- ルート=未分類(裁定 2 で確定)。
- textual タグ名ノード(値ノード 2 件以上)は、当該タグ付き画像がすべて値ノードへ吸われる
  ため直下表示がほぼ 0 件になる(値なし付与が残る場合のみ表示)。一貫した帰結だが驚きが
  あるため明文化+golden 固定する。
- 多重タグ画像は複数リーフに同時に現れる(最深「位置」は一意でない)。これは意図どおり。

**疑い(未検証・fix 時に確定)**:

- 階層ペインの件数表示のモード追随(下記 gate① 論点 a)。
- 既存の子件数計算の実装位置が差集合の計算に直接再利用できるか(構造は同一のはずだが
  プローブで確認する)。

## 4. 是正方針(案 — 着手時確定)

1. **CAD(ViewPrismUI)**: image_tab.md タグビュー節+mock にトグルを追加。置き場所=ビュー軸
   選択時のツールバー(表示軸セレクタ近傍)。ECO-027 狭幅収納との整合(トグルの畳み挙動)も
   CAD で確定。文言案(CAD で最終確定):
   - ja: 「すべて」/「未分類」(セグメント切替。ツールチップ=「下位の分類に属する画像も表示」/
     「下位のどの分類にも属さない画像のみ表示」)
   - en: "All" / "Unclassified"(tooltip = "Show all images under this node" /
     "Show only images not classified further")
   - i18n キー案: `view.displayMode.all` / `view.displayMode.unclassified` + tooltip 2 キー
2. **仕様 §2.4 追補**: 新 REQ(表示モード 2 値・既定=すべて・display(N) 定義・ルート=未分類・
   textual タグ名ノード帰結・settings.json 記憶=ビュー id 毎・パッケージ非搬送)。
3. **実装**: 表示集合の解決(`ViewMatched` 呼び出し文脈)に差集合を追加+トグル UI+
   settings.json 読み書き。DB・パッケージ・スナップショット非接触。
4. **オラクル**: 既存固定行不変(R6)。新規行=境界: ルート(未分類)・値 0/1/2 件・多重タグ
   画像が複数リーフに属す場合・モード切替の往復・settings.json 記憶の復元。

## 5. 影響 BOM

- CAD: ViewPrismUI `docs/screens/image_tab.md`+mock(gate① 対象)
- spec: `20-spec.md` §2.4 新 REQ(番号は fix 時採番)
- E-BOM: E-GRAPH-003 / E-EVAL-002(意味論の追補 note)・E-UI-NODEGRAPH-025 /
  E-UI-AXIS-NAV-040 / E-UI-BROWSE-022(surface)・E-SETTINGS-013(記憶キー追加)
- 実装: ImageTabViewModel(表示集合解決+トグル状態)・ImageTabView.axaml(トグル UI・
  i18n 配線=ECO-079/080 の lint が機械ゲート)・SettingsService 系
- i18n: ja/en 各 4 キー前後
- DB・マイグレーション・パッケージ(REQ-093)・スナップショット(REQ-092): **変更なし**(裁定 1)
- 既存固定オラクル: 変更なし(R6)

## 6. 残ゲート

- **gate①(裁定+CAD)**: ✅ **合格(2026-07-14 maintainer・実機モック確認)** — 詳細 §9
- **gate②(golden)**: 実機で「すべて/未分類」切替・ルート未分類・textual 値ノード帰結・
  ビュー毎記憶の復元・**狭幅ツールバー収納(セグメント込み)** を maintainer 確認。
  是正+機械受入は完了(§7)— golden 待ち。

## 7. 実施記録(2026-07-14 — 是正+機械受入完了・golden 待ち)

**プローブ先行(R5)**: CpUi084DisplayModeTests 新設(ECO-056 様式=コマンドはリフレクション解決)。
是正前実測= **6/6 不合格**(全て「SetDisplayModeUnclassifiedCommand が存在しない=表示モード導線の
不在」)→ 是正後 7/7 緑転(active 追随の VC probe 1 本を追加)。

**是正内容(最小 diff・真因構造=機能不存在の解消)**:

- 仕様: §2.4 へ **REQ-094** 新設(表示モード 2 値・display(N) 定義・ルート=未分類・チップ件数追随・
  専用空状態・母集合一致・settings 記憶=パッケージ非搬送)
- Core: `AppSettings.ViewDisplayModes`(**ViewDisplayModeMap=値等価辞書**。素の Dictionary は参照等価で
  CP-SET-009 の record 全体等価ラウンドトリップ契約を壊すことを実測→値等価型で契約保持)
- VM(ImageTabViewModel): `_viewUnclassified` 状態・`Unclassified()` 減算ヘルパ(直下の子のみ=仕様の
  上位集合性質)・Recompute の view 分岐(childMatched を件数と減算で共用=ECO-026 within 最適化維持)・
  チップ件数のモード追随・`ShowUnclassifiedEmpty`(汎用 ShowEmptyMessage と排他)・
  `SetDisplayModeAll/Unclassified` コマンド(切替で選択クリア=ClickChip 同型)・LoadViewAsync で復元・
  `AllLoadedImagesInContext` も表示集合に一致(タグ編集母集合・ビューアー順)
- axaml: DisplayModeSegment(segmented/segBtn 共有部品・AxisTrigger 直後・`ShowDisplayModeToggle` で
  ビュー軸のみ)+未分類専用空状態ブロック。文言は全て Loc バインド(K-AVALONIA/ECO-080 lint 適合)
- i18n: ja/en 各 6 キー(view.displayModeAll/Unclassified/AllTip/UncTip/unclassifiedEmpty/EmptyHint)
- 類似検索スコープ(REQ-087)は**現状維持**= matched(N) 全体(表示モード非追随。仕様化していない挙動を
  変えない — 必要なら別途裁定)

**機械受入**: build 0/0・**Tests 672/672 ×3 連続全緑**(新規 CpUi084 7 本+CpSet009 V4 1 本込み)・
Oracle 109+2skip(R6 不変)・validate_bom 0/0。

**セルフゴールデン(R7)**: CAD 側に visualContract 節を lazy 遡及で新設(image_tab.md VC-IMG-1〜5)し、
probe を先行生成。並置突合の結果=**転写漏れ 0**:

| VC | 検査 | 結果 |
|---|---|---|
| VC-IMG-1 セグメント形態・ラベル・active | headless 実レイアウト+active クラス追随(視覚言語= segBtn 共有部品に委譲=golden 承認済) | ✅ |
| VC-IMG-2 FS 軸非表示 | 同一 Window で軸切替→ IsVisible 出没 | ✅ |
| VC-IMG-3 狭幅で可視・畳みなし | 760px で可視+両ラベル幅>0 | ✅ |
| VC-IMG-4 専用空状態 | ShowUnclassifiedEmpty=true / 汎用と非重複。文言は Loc キー経由で mock と完全一致 | ✅ |
| VC-IMG-5 件数追随 | チップ 2⇄1(mock 実測 34⇄27 と同型) | ✅ |

**スコープ外所見(R3)**: Headless ハーネスのスレッドアフィニティ 2 態(ECO-082/083 ファミリー新知見)を
51-cheat-log へ記録(①単独実行の初期化順序 race=FailFast 監視の実運用 2 例目 ②worker 生成 Brush の
compositor 参照死=タグ色チップ描画の初クラスで顕在化)。テスト側書法で決定化済み・製品コード無関係。

## 8. GF-084-01 是正記録(2026-07-14 — golden 所見→同日是正)

**所見(maintainer 実機キャプチャ・mock 並置)**: en「Unclassified」がセグメントからはみ出しクリップ・
ja も mock(padding 0 13px)比で密着。

**工程診断=実装層(部品の取り違え)**: テキストセグメントの正部品 `segBtnText`(Padding 14,0・
内容幅・整理トレイ 類似/条件 切替で golden 承認済み・Components.axaml 導入コメントに「固定幅でなく
内容に合わせる」と明記)が既存なのに、アイコン用 `segBtn`(**Width=38 固定・Padding 0**)を流用した。
R7 並置の盲点=VC-IMG-1 が「segBtn 共有部品に委譲」とだけ書き、**文字量(特に en)の収まり**を検査
次元に持たなかった(ラベル可視・active 追随は検査済みだった)。

**プローブ先行(R5)**: 日英両ロケールの headless 実レイアウトで余白を実測する Theory を追加
(TestLoc.En 新設)。是正前赤= **ja/en とも「ボタン幅 38.0=ラベル幅 38.0・余白 0px」**
(en はみ出しの正体=固定幅クリップ)→ 是正後 9/9 緑転。

**是正(最小 diff)**: DisplayModeSegment の 2 ボタンを `segBtn`→`segBtnText` へ差し替え
(TextBlock 子→Content バインド・整理トレイと同型)。スタイル定義・VM・意味論は不変。

**再発防止**: CAD VC-IMG-1 へ「ラベルは日英とも切れない・密着しない(テキストセグメント=
segBtnText 系部品)」の検査次元を追補。機械側は上記 Theory(CP-DISPMODE-084 へ追記)で恒久 pin。

**副産物(ハーネス恒久対処)**: GF 是正後のフル run で、Headless セッション初期化 race
(51-cheat-log 2026-07-14 ①)が**テスト集合の増加により 3/3 定常発火へ転化**(初期化が最初の
Dispatch まで遅延される構造×クラス並列実行)。HeadlessApp へ **xunit v3 AssemblyFixture**
(SessionInitFixture)を追加し、**どのテストよりも先に初期化 Dispatch を同期完了**させて順序を
構造的に決定化。注: 初案の `[ModuleInitializer]` 同期待ちはローダーと dispatch コールバックの
相互待ちでデッドロックする(実測: 起動前ハング→HangDump 発火=最終安全弁の実運用)。
適用後フル run ×6 連続全緑(675/675)。

## 9. gate① 合格記録(2026-07-14)

CAD 改版(ViewPrismUI `bf6d4cf`: mock `ViewPrism2 画像タブ.dc.html`+`docs/screens/image_tab.md`
表示モード節新設)を maintainer が実機モックで確認し承認。論点の確定:

| 論点 | 裁定 |
|---|---|
| (a) 件数表示 | **追随** — 未分類モードではチップ件数=その子自身の未分類件数(モック実測 34⇄27) |
| (b) 形態・位置 | セグメントコントロール(グリッド/リスト同型)・表示軸セレクタ直後・ビュー軸のみ表示 |
| (c) 文言 | ja「すべて / 未分類」・en 案「All / Unclassified」+ツールチップ 2 種(CAD 記載) |

**承認時の追加指摘(CAD へ反映済み)**: ツールバーは既にレスポンシブ収納(ECO-027)を持つため、
セグメント追加分の幅圧迫を考慮すること。→ CAD 契約化: セグメントは収納段階の対象外(畳まない・
非表示にしない)・代わりに入口ボタンのラベル畳み/`…` 退避のしきい値がセグメント幅分早く発火してよい
(ECO-027 確定契約①②③不変)・しきい値再調整は「ビュー軸+セグメント表示」=最混雑状態で実測・
FS 軸の収納挙動は不変。**/eco-fix ではこの収納挙動をプローブ対象に含める。**

機械検証(モックロジック直接駆動・全 PASS): root すべて35/未分類1(未タグのデモ画像)・EF ノード 34/27・
チップ追随・タグ付与で 27→26 動的減算・全分類済みで空状態・FS 軸トグル非表示・選択母集合追随。
副産物の知見: `.dc.html` は dc-runtime が `fetch(location.href)` を使うため **file:// 直開き不可**
(HTTP 配信で確認する。standalone 版と異なる)。
