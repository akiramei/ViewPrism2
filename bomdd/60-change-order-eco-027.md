# ECO-027 — 画像タブ ツールバーの狭幅レスポンシブ収納(IMG-014)

- **type**: 機能追加(UI レイアウト・NFR「狭幅で重ならない」)+ CAD 準拠是正(表示列のモード非表示)
- **status**: implemented
- **golden**: approved(CP-UI-G1・maintainer 実機・2026-07-03)
- **baseline**: ECO-026 クローズ後(main `e21175b`)
- **bom_rev**: v4.0(eco:ECO-027)
- **authority**: ViewPrismUI(CAD 単一権威)
  - モック(収まりのみ権威): `資料/画像タブ/ViewPrism2 画像タブツールバーレスポンシブル (standalone).html`
  - CAD: `docs/screens/image_tab.md` §「ツールバー」→「ツールバーのレスポンシブ収納(狭幅対策)」/ §「レイアウト不変条件」
  - 判断台帳: `docs/review_points.md` IMG-014 / SRC-007・実装ハンドオフ `docs/handoff/img-014_toolbar_responsive.md`

## 背景 / 課題
画像タブのツールバーは、アプリ幅が狭くなるとコントロールが重なる/潜り込む。使えるツールバー幅は**ウィンドウ幅だけで決まらない**(左ペイン折り畳み 276/64・右ペイン[タグ編集/整理トレイ]の開閉で中央ペイン幅が変わる)。そのため CSS メディアクエリ相当(ビューポート幅)では判定できず、**ツールバー実測幅**で段階収納する必要がある。

## 実装方式(as-built)
判定ロジックは VM(`ImageTabViewModel`)に集約して unit 検査可能にし、実測供給は View(`ImageTabView`)責務とする(`ResizeObserver` 相当=レイアウト確定ごとに実測)。

- **段階収納(段階フラグ・content 幅=実測幅−水平パディング36)**
  1. ラベル畳み(<約820px): 入口ボタン「タグ編集/整理/作業」をアイコンのみ化(`CollapseEntryLabels`)・ToolTip でツールチップ維持。
  2. 退避(<約640px): 「整理」を`⋯`(その他)メニューへ退避(`StowOrganizeToMenu`・`ShowOrganizeEntryButton`=false)。
  3. 回り込み(単一行に収まらない時): 右クラスタ[ソート+グリッド/リスト]を2段目へ(`ToolbarWrapped`→Grid の行/列/スパン切替)。左クラスタも極狭では WrapPanel が内部折り返しし、離脱ボタンをクリップしない。
- **実測**: View が `LayoutUpdated` でツールバー実測幅を VM へ供給(`ReportToolbarWidth`)。回り込みは左右クラスタの自然幅合算 > 使える幅で判定し `SetToolbarWrapped`(ヒステリシス24px)。段階しきい値近傍のばたつきは epsilon(2px)+ヒステリシス帯(24px)で抑制。
- **tier3 構造**: 中央ツールバーを DockPanel→2 列×2 行 Grid へ。広い時=左(row0/col0)+右(row0/col1・右寄せ)=従来と同一レイアウト。狭い時=左(row0 全幅)+右(row1 全幅・右寄せ)。左クラスタは WrapPanel(旧 `Spacing="14"` は子 `Margin` で等価再現=通常幅は視覚不変)。
- **CAD 準拠是正(実機所見由来)**: モード中も「表示列」が出ていたのは CAD 逸脱(image_tab.md「モード中に残すのは表示軸・ソート・グリッド/リスト・終了だけ」)。狭幅で右クラスタへ潜り込む主因だった。`ShowColumnsEntry = CanEditColumns && !InAnyMode` でモード中は隠し、モード突入時に開いていた列ピッカーは閉じる。

## 確定契約(不変条件・px 値でなく挙動)
1. どの幅でもコントロールが重ならない/潜り込まない(右ペイン開でも)。
2. 畳む順序は低優先から=ラベル → 低優先アクション(整理)の`⋯`退避 → 回り込み(いきなり回り込み/重なりへ逃げない)。
3. モードの離脱/実行ボタン(◯◯を終了・ゴミ箱へ移動 等)のラベルは狭幅でも維持(アイコンのみの曖昧表現にしない)。
   → しきい値 820/640 はモック由来の目安で調整可。px 値を変えても ①②③ を崩さなければ mock 是正に当たらない。

## 退行防止
ソート導線は一切改変せず file_list v2(ECO-025/FL-003)のまま。本モックに残存する旧固定ソート(名前/更新日/サイズ)は復活させていない。

## impacted_bom
- `20-spec.md` §2.6(ツールバー): 狭幅レスポンシブ収納 as-built・モード中は表示列も隠す。
- `30-ebom.yaml` E-UI-MODE-041: since ECO-027 不変条件2件(表示列モード非表示=CAD 準拠 / 実測幅による段階収納+回り込み)・acceptance_refs += CP-TOOLBAR-RESPONSIVE-027。
- `33-control-plan.yaml` CP-TOOLBAR-RESPONSIVE-027(新設・unit 決定論ガード)。視覚は CP-UI-G1(golden)。
- ソート/列描画/表示列モデル(FL-003・ECO-025)・グリッド仮想化(ECO-026)は非改変。

## verification
- build 0警告/0エラー・Tests 526(+CP-TOOLBAR-RESPONSIVE-027 13)・Oracle 100+2skip・validate_bom 0/0。
- 視覚: maintainer 実機 golden 承認(CP-UI-G1・2026-07-03)。往復2回で是正=(1回目)モード中の表示列潜り込み→CAD 準拠でモード非表示化+右クラスタ回り込み、(2回目)極狭で離脱ボタン「タグ編集を終了」がクリップ→左クラスタも WrapPanel 内部折り返しへ。
- Oracle S-01〜S-31 不変(Core 意味論不変・レイアウトのみ)。

## decisions
- **SCOPE**: ツールバーの収まり(段階収納)のみ。作業/修復/削除/ゴミ箱の詳細機能(IMG-010)は範囲外=継続未確定。既存の出し分け(排他隠し=ECO-014 §8)は維持。
- **MEASURE_NOT_VIEWPORT**: 判定は実測幅(左右ペイン状態を反映)。ビューポート幅メディアクエリは不可。
- **REFLOW_OVER_CLIP**: 極狭では回り込み/内部折り返しで対処し、離脱ボタンをクリップしない(契約③)。中央 ClipToBounds は最終保険として残置。

## process retro
- 実機 golden 往復2回。1回目所見(表示列)は工程診断で **CAD 逸脱(モード中の表示列)** と判明→CAD 権威を先に確認して是正(retro 教訓「所見→まず工程診断→CAD 権威を先に直す」に合致)。
- 2回目所見(離脱ボタンのクリップ)は自設計の漏れ(右クラスタのみ回り込ませ左クラスタの入りきらないケースを見落とし)。契約③(離脱ラベル維持)を満たすには左も折り返し可能にする必要があった=最初にモックの単一 flex-wrap の含意(全項目が独立に折り返す)を左右双方へ適用すべきだった。
