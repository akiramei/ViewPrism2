# Change Order — ECO-017(画像タブ 機能完成③: 作業ボタン=3つ目の排他文脈モード)

> **機能完成 ECO 第3弾**。UQ-I07 の最後の残項目「**作業**」ボタンを決着し、新 `ImageTabView` のツールバーへ配線する。
> **入力 CAD(モック)**: `ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ作業ボタン.html`(作業モードの動くモック)。BomDD のソフトウェア版 CAD として、モック+UI-IR が製造の原器([[mock-ui-ir-is-cad]])。
> **帰属: design_decision(maintainer 裁定 2026-06-29)**。UQ-I07 の `作業` を「3つ目の排他文脈モード(作業対象セットの蓄積)」として確定。
> **スコープ: 作業モード + 作業対象の蓄積のみ**。`workTargets` の消費先である**作業タブ本体(上部ナビのタグ/画像/作業)は今回スコープ外**(モック明記 `// 作業タブは今回スコープ外`)。詳細パネル/ノート編集(REQ-043)は引き続き**別 ECO**。**本 ECO 完了後も原典撤去は保留**(詳細/ノートの行き先が決まるまで=ECO-013 の裁定を継続)。

## 0. 変更前 baseline
- As-Built: ECO-016(surface 部品体系化)後。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は **surface への入口追加(配線)+ デザインシステム部品追加のみ**。スキーマ・固定オラクル・**Core 意味論不変**(作業モードは E-UI-BROWSE-022 の既存選択機構を再利用するだけで、新 Core サービスを足さない)。

## 1. 変更要求
- ECO-ID: **ECO-017**
- 発生契機: ECO-013 原典撤去ブロッカー分析の残・UQ-I07 の最後の未決「作業」。maintainer が ViewPrismUI で作業ボタンの UI/UX(動くモック)を設計完了 → 私が形式化(UI-IR/UI-BOM→E-BOM→ECO)+ 製造 + golden(整理 ECO-014 と同じ分業)。
- 内容: 「作業」ボタンを**タグ編集/整理に並ぶ3つ目の排他文脈モード**として実装。作業中はグリッドが選択可能になり、「追加」ボタンで選択を `workTargets` セットへ蓄積する。「作業対象 N 枚」チップで件数を示す。
- 種別: **設計決定(新表面・CAD モック由来)**。原典 view-prism に作業タブ機能はないため要求トレース(REQ-*)は持たない new-surface。

## 2. モック観測(CAD = 原器)
作業ボタンモック DCLogic の確定挙動(`bomdd/ui/image-tab/ui-ir.json` に抽出):
- `workMode`(初期 false)を「作業」ボタンでトグル。active = 緑塗り(#16a34a・白字)・ラベル `作業` / `作業を終了`。
- `toggleWork`: ON で `editMode:false, selected:[], expandTag:null`(他文脈モードと排他・選択クリア)。
- `inSelect = inEdit || inWork`: **作業モードでもグリッドが選択可能**(タグ編集と同じチェック/SHIFT 範囲機構)。フォルダはナビ。閲覧(どのモードでもない)はビューアー(本実装ではダブルクリック)。
- `追加`(addToWork): 作業モード中のみ出現。選択あり=緑塗り+件数バッジ(`workSelCount`)/選択なし=淡緑 disabled(border #cfe7d4・bg #eef8f1・文字 #9fc3ac)。押下で `selected` を `workTargets` Set へ和集合追加し選択クリア。
- `作業対象 N 枚` チップ(workTargetChip): `workTargets.length>0` で表示。緑系(bg #eafaf0・border #c2ecd2・文字 #0f8a4d・チェックアイコン)。
- **構造差**: 作業モードは**右ペインを開かない**(追加ボタン+チップはツールバー内・中央列はフル幅のまま)。編集/整理が右ペインを開く点と異なる。
- **モック明記のスコープ外**: `workTargets: [], // imageId[] -> 作業タブ対象（作業タブは今回スコープ外）`。
- **data 層と描画層の食い違い**: モックは `isWorkTarget` / `workBadgeStyle`(作業対象セルへの緑バッジ)を data で算出するが、グリッドテンプレートはこれを描画しない(`{{ it.workBadgeStyle }}` バインドが無い)。実際に描画されるのは「作業対象 N 枚」チップのみ。

## 3. disposition(maintainer 裁定 2026-06-29)
| ID | 裁定 | 選択肢 | 根拠 | 同期先 |
|---|---|---|---|---|
| **W1 ツールバー表示** | **既存の排他隠し方式に統一** | (a)排他隠し統一 / (b)モック準拠で3ボタン常時表示 | 作業は右ペインを開かず中央フル幅だが、編集/整理と同じ集中・排他可視化規律(ECO-014 §8)を作業へ拡張する方が一貫。作業中は他モード入口・⋯ を隠し「作業を終了」+「追加」+「作業対象」チップ+表示軸/ソート/レイアウトのみ残す | E-UI-MODE-041 / `InAnyMode` を作業へ拡張 |
| **W2 個別セルバッジ** | **描画モック準拠=バッジ無し** | (a)バッジ無し / (b)data 層の意図を採用し緑バッジ | [[mock-ui-ir-is-cad]]「適合は描画モック基準で測る」。モック data 層は用意するがテンプレ未描画 → 出さない。蓄積件数はチップで示す | E-UI-MODE-041 invariant |
| **W3 スコープ** | **作業モード + 蓄積のみ** | (a)蓄積のみ / (b)永続化も含める | モック明記の境界に忠実。`workTargets` はセッション内 `List<string>`(Set 意味論)。作業タブ受け渡し・永続化は別ループ | スコープ外 §6 |

## 4. BOM 改訂
### E-BOM(`30-ebom.yaml`)
- `E-UI-MODE-041`(画像タブ文脈モード統制): 作業モードを **3つ目の排他文脈モード**として追記。
  - invariant 追加: `since ECO-017`: 「作業」= タグ編集(E-UI-TAGASSIGN-029)/整理(E-UI-SIMILARITY-035+E-UI-MERGE-036)に並ぶ3つ目の排他文脈モード。作業中はグリッドが選択可能(E-UI-BROWSE-022 の選択機構を再利用)になり、選択を `workTargets` 集合へ和集合追加する「追加」アクション(選択あり時のみ活性・追加後に選択クリア)と「作業対象 N 枚」チップを持つ。**追加判定・和集合・選択クリア・件数は描画から独立した決定論ロジックとして unit 検査可能にする**。
  - invariant 追加: `since ECO-017`: 作業モードは**右ペインを開かない**(中央フル幅)。だが ECO-014 §8 の排他隠し規律を作業へ拡張する(作業中はツールバーが他モード入口・⋯ を隠し、「作業を終了」+「追加」+「作業対象」チップ+表示軸/ソート/レイアウトのみ残す)。
  - invariant 追加: `since ECO-017`: 作業対象セル個別の視覚マーク(バッジ)は出さない(描画モック準拠=W2)。蓄積件数はツールバーのチップで示す。`workTargets` はセッション内蓄積のみ(永続化・作業タブ受け渡しはスコープ外=W3)。
  - `depends_on` に変更なし(E-UI-BROWSE-022 既載)。
- `E-UI-BROWSE-022`: 意味論不変(**作業モードでもクリック=既定選択になるのみ**。選択ロジック・母集合・選択順は不変)。invariant に作業モードのクリック意味論接点を1行追記。
- `E-DESIGN-028`: 緑系塗りボタン(作業/追加)+緑系チップ(作業対象)を shared component として追加(Components.axaml)。
- 固定オラクル: 追加なし(S-01〜S-31 不変)。スキーマ不変。

### UI-IR / UI-BOM(`bomdd/ui/image-tab/`)
- `ui-ir.json`: TMP-UI-ACT-0062(作業)を「(詳細未定)」から確定挙動へ更新 + TMP-UI-ACT-0064(追加)新設・TMP-UI-CMP-0036(追加ボタン)/0037(作業対象チップ)・TMP-UI-STA-0033(作業モード)/0034(作業対象蓄積)・domainConcept(workTargets)。
- `ui-bom.json`: 上記を昇格(作業/追加=E-UI-MODE-041 specification_link+E-UI-BROWSE-022 coreRef / ボタン・チップ=E-DESIGN-028 shared_component / 状態=acceptance_aspect)。
- `ui-trace-map.json`: mock locator(toggleWork/addToWork/workTargets)→ UI-IR/UI-BOM/E-BOM のトレース3行追加・既存ツールバー行を更新。
- `unresolved-questions.md`: UQ-I07 作業=決定。決定ログ追記。

### CAD(ViewPrismUI)
- `docs/screens/image_tab.md`: ツールバー節に「作業」モード節を追加(3裁定明記)。一次モック `資料/画像タブ/ViewPrism2 画像タブ作業ボタン.html` を取込(別リポ・maintainer コミット)。

## 5. 製造(`ImageTabViewModel` + `ImageTabView` + Components.axaml)
- `ImageTabViewModel`: `_workMode`/`_workTargets`(`List<string>`)state。公開契約 `WorkMode`/`WorkButtonLabel`/`HasWorkSelection`/`WorkSelCount`/`HasWorkTargets`/`WorkTargetLabel`/`CanAddToWork`。コマンド `ToggleWork`/`AddToWork`。`InAnyMode` を `_editMode || _organizeMode || _workMode` へ拡張。`ToggleEdit`/`ToggleOrganize` で作業解除。グリッド項目の `selectable` を `inSelect=_editMode||_workMode` へ(タグドット表示も `!inSelect`)。`HandleItemClick` に作業=選択分岐。
- `ImageTabView.axaml`: 作業ボタンを `ToggleWorkCommand` へ配線(active=WorkMode・ラベル=WorkButtonLabel)+「追加」ボタン(WorkMode 時可視・CanAddToWork で活性・件数バッジ)+「作業対象 N 枚」チップ(HasWorkTargets 時)。edit/organize/⋯ の `IsVisible` を作業モードでも隠れるよう更新。
- `Components.axaml`: `toolbarBtn.workActive`(緑塗り)・`Button.workAddBtn`(+`.ready`)・`Border.workTargetChip` を追加。
- テスト: `CpUiG1WorkModeTests`(排他・選択再利用・追加の和集合/選択クリア・活性ガード・チップ件数)。

## 6. 受入(計画)
- browse 時に「作業」→ 作業モード(緑 active・グリッドが選択可能・他モード入口/⋯ が隠れる)。
- 画像を選択 →「追加」が活性(件数バッジ)→ 押下で「作業対象 N 枚」チップが増え選択が解除される。
- 作業 ON でタグ編集/整理が解除される(排他)。逆も同様。
- 個別セルに作業バッジは出ない(チップのみ)。
- 回帰: `dotnet test tests/ViewPrism2.Tests` + `tests/ViewPrism2.Oracle`(S-01〜S-31)退行ゼロ・build 警告0。実機 golden(作業モード遷移・追加・チップ・排他)。

## 7. スコープ外(後続)
- **作業タブ本体**(workTargets の消費先): モック明記スコープ外。別ループ。
- **workTargets の永続化**: セッション内蓄積のみ(W3)。
- **詳細パネル/ノート編集(REQ-043)**: 別 ECO(原典撤去の最後のブロッカー)。
- **原典撤去**: 詳細/ノートの行き先が決まるまで保留(ECO-013 の裁定継続)。

## 8. provenance / lesson
- 本 ECO で **UQ-I07 の全項目(整理=ECO-014 / ⋯=ECO-015 / 作業=ECO-017)が決着**。残る原典撤去ブロッカーは詳細/ノート(REQ-043)のみ。
- lesson: モックの **data 層と描画層が食い違う**ケース(isWorkTarget/workBadgeStyle を用意するが未描画)では、[[mock-ui-ir-is-cad]] の「適合は描画モック基準」に従い描画を原器とする。data 層の意図は UI-IR の note と UQ に残し、必要なら後続 ECO で designIntent として再起票できる形にする(今回は W2 でバッジ無しに裁定)。
- lesson: 作業は右ペインを開かない構造だが、ツールバー出し分けは「右ペインの幅制約」ではなく「集中・排他可視化」という上位の設計規律(ECO-014 §8)に帰属させ、作業へ一貫適用した(W1)。
