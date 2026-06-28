# Change Order — ECO-018(画像タブ ⋯メニュー再構成 + 削除モード=ゴミ箱へ移動)

> **機能完成 ECO 第4弾**。画像タブ ツールバー `⋯`(その他)メニューを **修復 / 削除 / ゴミ箱** の 3 項目へ再構成し、新たに「**削除**」= タグ編集/整理/作業に並ぶ 4 つ目の排他文脈モードを追加する。削除モードは選択画像を「**ゴミ箱へ移動**」(normal→deleted のユーザー起点ソフト削除・物理非破壊 INV-009・復元可)する。
> **入力 CAD(モック)**: `ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ削除ボタン.html`。
> **帰属: design_decision(maintainer 裁定 2026-06-29)**。
> **モックの是正(maintainer 指摘)**: モックは `⋯` の **修復** と **ゴミ箱(トラッシュ)** をモード(repairMode / trashOpen オーバーレイ)に入れているが、これは**正しくない**。本来は**既存モーダル(ポップアップ)**が開く(ECO-015 の挙動)。よって修復/ゴミ箱はモックの挙動を採らず既存モーダルのまま。**削除**モード(`enterDelete`/`deleteSelected`=ゴミ箱へ移動)はモックが正しい新機能。

## 0. 変更前 baseline
- As-Built: ECO-017(作業ボタン)後。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は **surface(⋯メニュー再構成+削除モード)+ Core の 1 操作追加(normal→deleted ソフト削除)+ DS 部品**。スキーマ不変。

## 1. 変更要求
- ECO-ID: **ECO-018**
- 発生契機: maintainer が ViewPrismUI で `⋯` メニュー(修復/削除/ゴミ箱)+削除モードの UI/UX(動くモック)を設計。私が形式化(UI-IR/UI-BOM→E-BOM→ECO)+ Core 1 操作 + 製造 + golden。
- 内容: (1) `⋯` メニュー = 修復/削除/ゴミ箱 へ再構成。(2) 「削除」= 削除モード(選択 → ゴミ箱へ移動 = ソフト削除)。(3) 修復/ゴミ箱 = 既存モーダルのまま(モックのモード化は不採用)。
- 種別: **設計決定(新表面 + Core ソフト削除遷移)**。

## 2. モック観測(CAD)+ 是正
削除ボタンモック DCLogic:
- `⋯` メニュー(3 項目): `enterRepair`(修復)/`enterDelete`(削除・赤)/`openTrash`(ゴミ箱・件数バッジ `hasTrash`/`trashCount`)。
- `enterDelete` → `deleteMode`(editMode/workMode/repairMode を解除・選択クリア)。削除ツールバー = `exitDelete`(削除を終了)+ `deleteSelected`(「ゴミ箱へ移動」・赤 `delBtnStyle`・件数バッジ)。
- `deleteSelected` → 選択を trash へ移動(ソフト削除)+ 選択クリア。
- **是正(maintainer)**: モックの `enterRepair`(repairMode インペイン)と `openTrash`(trashOpen オーバーレイ)は**誤り**。修復/ゴミ箱は**既存モーダル**(`IWindowService.ShowRepairAsync`/`ShowTrashAsync`・ECO-015)で開く。`deleteMode` と「ゴミ箱へ移動」は正しい。

## 3. disposition(maintainer 裁定 2026-06-29)
| ID | 裁定 | 根拠 | 同期先 |
|---|---|---|---|
| **D1 削除=ソフト削除** | 「ゴミ箱へ移動」= normal→deleted のソフト削除(物理非破壊 INV-009・復元可) | モックラベル「ゴミ箱へ移動」+ INV-009 + 既存トラッシュライフサイクル(復元/完全削除)。完全削除は従来どおりゴミ箱モーダル経由 | E-TRASH-038.DeleteToTrashAsync(新) |
| **D2 修復/ゴミ箱=既存モーダル** | モックのモード化(repairMode/trashOpen)は不採用。既存モーダルのまま(ECO-015) | maintainer 指摘=本来ポップアップ。修復(criteria/relink/復元)・ゴミ箱(復元/完全削除)は既存モーダルが完成済で再設計不要 | E-UI-MODE-041 / E-UI-REPAIR-039(不変) |
| **D3 削除=4つ目の排他モード** | 削除は ⋯ メニューから入る排他文脈モード(ツールバー入口なし)。排他隠し統一(ECO-014 §8/ECO-017)を削除へ拡張 | 作業と同型(選択再利用)。削除中は他入口・⋯ を隠し「削除を終了」+「ゴミ箱へ移動」のみ | E-UI-MODE-041 |
| **D4 確認なし即実行** | 「ゴミ箱へ移動」は確認ダイアログなしで即実行(モック準拠) | ソフト削除は復元可=可逆。モックも確認なし | — |
| **D5 ゴミ箱件数バッジ** | `⋯`「ゴミ箱」に deleted 件数バッジ | モック `hasTrash`/`trashCount`。選択コレクションの deleted 件数を表示 | E-UI-MODE-041 |

## 4. BOM 改訂
### Core E-BOM(`30-ebom.yaml`)
- `E-TRASH-038`: `DeleteToTrashAsync`(normal→deleted ソフト削除)を追加。normal 限定・status 更新のみ・物理非破壊(INV-009)・タグ/ID/特徴量不変・復元可。マージ/除外と並ぶ 3 つ目の deleted 入口。acceptance に **CP-TRASH-022** 追加。**固定オラクル候補(M4 で凍結検討)**。
### Surface E-BOM
- `E-UI-MODE-041`: `⋯` メニュー = 修復/削除/ゴミ箱(ECO-018)。修復/ゴミ箱 = 既存モーダル(ポップアップ)・ゴミ箱は deleted 件数バッジ。「削除」= 4 つ目の排他文脈モード(⋯ から入る・グリッド選択再利用・「ゴミ箱へ移動」で E-TRASH-038.DeleteToTrashAsync・排他隠し拡張)。
- `E-UI-BROWSE-022`: 選択許可フラグ(inSelect)に削除モードを追加(意味論不変・選択機構の再利用)。
- `E-DESIGN-028`: 赤系塗りボタン(ゴミ箱へ移動)+赤件数バッジ+ゴミ箱件数バッジを shared component に追加(Components.axaml)。
- 固定オラクル: S-01〜S-31 不変。スキーマ不変。

### UI-IR / UI-BOM(`bomdd/ui/image-tab/`)
- `ui-ir.json`: action(enter-delete / delete-to-trash / exit-delete)・state(delete mode / trash count)・component(削除メニュー項目・ゴミ箱へ移動ボタン・ゴミ箱件数バッジ)・⋯メニュー再構成・domainConcept(ソフト削除/ゴミ箱)。
- `ui-bom.json`: 昇格(削除=E-UI-MODE-041 specification_link + E-TRASH-038 coreRef / ボタン・バッジ=E-DESIGN-028 shared_component)。
- `ui-trace-map.json`: mock locator(enterDelete/deleteSelected/openTrash/trashCount)→ トレース。

### CAD(ViewPrismUI)
- `docs/screens/image_tab.md`: ツールバー「その他」節を 修復/削除/ゴミ箱 へ更新 + 削除モード節追加(別リポ・maintainer コミット)。一次モック取込。

## 5. 製造
- Core: `TrashService.DeleteToTrashAsync(imageId)`(normal→deleted・normal 以外 ValidationError・物理非破壊)。
- `ImageTabViewModel`: `_deleteMode`/`_trashCount` state。`DeleteMode`/`HasDeleteSelection`/`DeleteSelCount`/`CanDeleteToTrash`/`HasTrash`/`TrashCount`。`EnterDelete`/`ExitDelete`/`DeleteToTrash`/`RefreshTrashCountAsync`。`InAnyMode`・`InSelectMode`・`ShowEditEntry/ShowOrganizeEntry/ShowWorkEntry`・grid `inSelect`・`HandleItemClick` を削除へ拡張。ToggleEdit/ToggleWork/ToggleOrganize で削除解除。OpenTrash/OpenRepair/Initialize で件数更新。ctor に `TrashService` 注入(MainWindowViewModel/App DI/6 テスト構築サイト更新)。
- `ImageTabView.axaml`: ⋯ メニュー = 修復/削除(赤)/ゴミ箱(件数バッジ)。削除モードツールバー = 削除を終了 + ゴミ箱へ移動(赤・件数バッジ)。
- `Components.axaml`: `delMoveBtn`(+`.ready`+`:disabled`)・`delMoveBadge`・`trashCountBadge`。
- テスト: `CpTrash022Tests`(Core 3 件)+ `CpUiG1DeleteModeTests`(VM 5 件)。

## 6. 受入(計画)
- browse 時に `⋯` → 修復/削除/ゴミ箱。修復/ゴミ箱で既存モーダルが開く(モードに入らない)。ゴミ箱に deleted 件数バッジ。
- 「削除」→ 削除モード(他入口・⋯ が隠れる)→ 画像選択 →「ゴミ箱へ移動」が活性(件数バッジ)→ 押下で選択が normal 一覧から消え、ゴミ箱件数が増える。
- 削除でタグ編集/整理/作業が解除される(排他)。
- ゴミ箱モーダルで復元すると normal 一覧へ戻る(復元可=ソフト削除)。
- 回帰: `dotnet test`(Tests + Oracle S-01〜S-31)退行ゼロ・build 警告0。実機 golden。

## 7. スコープ外(後続)
- 削除の固定オラクル凍結(S-32 候補): M4。
- 詳細パネル/ノート編集(REQ-043): 別 ECO(原典撤去の最後のブロッカー)。
- 確認ダイアログ・一括 Undo 等の追加 UX: 必要になれば後続。

## 8. provenance / lesson
- lesson: モックが「正しくない」箇所の見極め。モックは UI 形状(⋯=修復/削除/ゴミ箱)を示すが、修復/ゴミ箱の**遷移をモード化**したのは誤りで、既存モーダル(ポップアップ)が正。CAD は形状の原器だが、**既存の完成済資産(モーダル)を作り直す指示ではない**ことを maintainer 裁定で確認(ECO-015 lesson の再適用=再設計は CAD がそれを示す領域=削除モードのみ)。
- lesson: 新しいユーザー起点の状態遷移(normal→deleted ソフト削除)は INV-009(物理非破壊)の woven 安全制約領域。既存 `ExcludeAsync`(missing→deleted)と対称に、status 更新のみ・ファイル不触で実装し、復元可の可逆操作として設計した。
