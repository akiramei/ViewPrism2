# Change Order — ECO-019(ゴミ箱モーダルを画像タブ内ポップアップへ作り直す)

> **機能完成 ECO 第5弾**。ECO-018 で `⋯`「ゴミ箱」が開いていた**既存トラッシュモーダル**(別ウィンドウ)を、画像タブ CAD デザイン言語の**画像タブ内 中央オーバーレイ(ポップアップ)**へ作り直す(整理トレイ ECO-014・修復と同型の「原典モーダルを作り直す」)。複数選択 / すべて選択 / 復元 / 完全削除 / ゴミ箱を空 を備える。
> **入力 CAD(モック)**: `ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブゴミ箱ポップアップ.html`。
> **帰属: design_decision(maintainer 裁定 2026-06-29)**。
> **重要なモック是正(maintainer 裁定)**: モックの確認ダイアログ文言「**元のファイルも削除され**、この操作は元に戻せません」は **INV-009(物理非破壊)と矛盾するため不採用**。完全削除は **DB 行のみ除去・物理ファイルは削除しない**(既存 `PermanentDeleteAsync`・INV-014・S-26)。文言は「画像ファイルは削除されません(DB から除去)。この操作は元に戻せません。」へ修正。**woven 安全制約はモック(CAD)より上位**であり、モック文言が安全制約を覆すことはできない。

## 0. 変更前 baseline
- As-Built: ECO-018(⋯メニュー再構成+削除モード)後。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は **surface(トラッシュ UI の作り直し)のみ**。Core 操作は既存(RestoreAsync / PermanentDeleteAsync)を再利用し**新規 Core なし**。スキーマ不変。

## 1. 変更要求
- ECO-ID: **ECO-019**
- 発生契機: maintainer が ViewPrismUI で新しいゴミ箱ポップアップ(画像タブ内オーバーレイ)を設計。私が形式化+製造+golden。
- 内容: ⋯「ゴミ箱」= 既存モーダル → **画像タブ内ポップアップ**。複数選択・すべて選択・復元・完全削除・**ゴミ箱を空(一括完全削除)**を追加。
- 種別: **設計決定(トラッシュ UI の作り直し・既存 Core 再利用)**。

## 2. モック観測(CAD)+ 是正
ゴミ箱ポップアップモック:
- 中央オーバーレイ(780×560)。ヘッダ=ゴミ箱/削除済み画像/件数バッジ/すべて選択(toggleSelectAll)/閉じる。
- グリッド: チェックボックス(青選択)+サムネ+名前+サイズのカード。複数選択(trashSel)。空状態あり。
- フッター: 左=選択件数(N 枚選択中 / 画像を選択して操作)+ ゴミ箱を空(emptyTrash・赤テキスト) / 右=復元(青・件数バッジ)・完全削除(赤・件数バッジ)。
- 完全削除/空は確認ダイアログ(confirmTitle/confirmBody/confirmCta)。
- **是正(maintainer)**: モック confirmBody「元のファイルも削除され…」は **INV-009 と矛盾=不採用**。物理非破壊の実挙動に合わせ文言を修正する。

## 3. disposition(maintainer 裁定 2026-06-29)
| ID | 裁定 | 根拠 | 同期先 |
|---|---|---|---|
| **T1 文言=INV-009 維持** | 完全削除/空の確認文言は「画像ファイルは削除されません(DB から除去)。この操作は元に戻せません。」。モックの「元のファイルも削除され」は不採用 | woven 安全制約 INV-009/INV-014 はモックより上位。実挙動(`PermanentDeleteAsync`=DB 行のみ・S-26 物理差分オラクル)と文言を一致させる。既存 `trash.purge.confirmMessage` も同趣旨 | 確認文言(ハードコード ja) |
| **T2 ポップアップ化** | ゴミ箱を画像タブ内オーバーレイへ作り直す(別ウィンドウモーダルを置換) | CAD 方向(整理トレイ ECO-014・in-app)。ECO-018 の `ShowTrashAsync` 別ウィンドウを `⋯`「ゴミ箱」から開く in-tab popup へ | E-UI-MODE-041 / E-UI-REPAIR-039 |
| **T3 複数選択+空にする** | 複数選択・すべて選択・ゴミ箱を空(一括完全削除)を追加 | モック新機能。既存モーダルは単一選択のみ・空にする無し | E-UI-REPAIR-039 |
| **T4 Core 再利用** | 復元=RestoreAsync をループ / 完全削除・空=PermanentDeleteAsync をループ | 既存 Core で充足。新規 Core 不要=INV-009 既証(S-26) | E-TRASH-038(不変) |

## 4. BOM 改訂
### Surface E-BOM(`30-ebom.yaml`)
- `E-UI-MODE-041`: `⋯`「ゴミ箱」は **画像タブ内ポップアップ**を開く(ECO-018 の別ウィンドウモーダルを置換)。
- `E-UI-REPAIR-039`: トラッシュ UI を画像タブ内ポップアップへ作り直す(複数選択・すべて選択・復元・完全削除・**ゴミ箱を空**)。完全削除/空は確認+**INV-009 非破壊明示「画像ファイルは削除されません(DB から除去)・元に戻せません」**(モックの物理削除文言は不採用)。状態遷移は E-TRASH-038(RestoreAsync/PermanentDeleteAsync)のみ経由。選択・一覧・空状態は描画から独立した決定論ロジックとして unit 検査可能(CP-UI-G1)。
- `E-DESIGN-028`: ゴミ箱ポップアップ部品(popup/header/footer・カード+チェック・復元(青)/完全削除(赤)/空(赤テキスト)ボタン・件数バッジ)を shared component に追加。
- `E-TRASH-038`: 不変(Core 再利用)。固定オラクル S-01〜S-31 不変。スキーマ不変。

### UI-IR / UI-BOM(`bomdd/ui/image-tab/`)
- `ui-ir.json`: trash popup の action(open/close/select/select-all/restore/purge/empty)・state(open/selection)・component(popup/card/footer ボタン群)・⋯ゴミ箱の挙動更新・domainConcept(ゴミ箱複数操作)。
- `ui-bom.json`: 昇格(E-UI-REPAIR-039 specification_link + E-TRASH-038 coreRef / 部品=E-DESIGN-028 shared_component)。
- `ui-trace-map.json`: mock locator(trashOpen/toggleSelectAll/restoreSelected/purgeSelected/emptyTrash/confirm)→ トレース。**confirmBody の INV-009 是正を明記**。

### CAD(ViewPrismUI)
- `docs/screens/image_tab.md`: ゴミ箱ポップアップ節を追加(複数選択/すべて選択/空にする・**完全削除は物理非破壊=文言是正**)。一次モック取込(別リポ・maintainer コミット)。

## 5. 製造
- `ImageTabViewModel`: trash popup state(`TrashOpen`/`TrashPopupItems`/`_trashSel`)+ 公開契約(件数/選択/ラベル/活性)。コマンド `OpenTrash`(→ in-tab popup・`ShowTrashAsync` 置換)/`CloseTrash`/`ToggleTrashItem`/`ToggleTrashSelectAll`/`RestoreSelectedTrash`/`PurgeSelectedTrash`(確認)/`EmptyTrash`(確認)。`LoadTrashItemsAsync`(GetByFolderAsync→deleted・絶対パス算出)・`RefreshTrashSelection`(その場更新)。
- `TrashPopupItemVM`(ObservableObject・IsSelected その場更新)。
- `ImageTabView.axaml`: ルートを Panel でラップし中央オーバーレイ追加(ヘッダ/グリッド/空状態/フッター)。
- `Components.axaml`: trashPopup/Header/Footer・trashCard・trashThumb(+selected)・trashCheck(+selected)・trashRestoreBtn(+ready・青)・trashRestoreBadge・trashEmptyBtn(赤テキスト)。
- `App.axaml`: RestoreIcon 追加。
- 確認文言は INV-009 忠実(ハードコード ja・`PurgeSelectedTrash`/`EmptyTrash` 内)。
- テスト: `CpUiG1TrashPopupTests`(6 件)+ `CpUiG1MaintenanceMenuTests` を popup 化へ更新。

## 6. 受入(計画)
- ⋯「ゴミ箱」→ 画像タブ内ポップアップが開き(別ウィンドウではない)deleted 一覧+件数。空なら空状態。
- 複数選択・すべて選択 → 復元(青)/完全削除(赤)が活性(件数バッジ)。ゴミ箱を空(赤テキスト)。
- 完全削除/空 → 確認ダイアログ(**「画像ファイルは削除されません(DB から除去)」**)→ 承認で DB 行削除・物理不変。却下で無操作。
- 復元 → normal はグリッドへ戻る・missing は戻らない(存在分岐)。
- 回帰: `dotnet test`(Tests + Oracle S-01〜S-31)退行ゼロ・build 警告0。実機 golden。

## 7. スコープ外(後続)
- 詳細/ノート(REQ-043): 別 ECO(原典撤去の最後のブロッカー)。
- ゴミ箱内の操作の固定オラクル(復元/完全削除は S-26/S-29/S-30 で既証・popup は表面)。

## 8. provenance / lesson
- lesson(重要): **モックの文言が woven 安全制約と矛盾する場合、安全制約が勝つ**。本件 confirmBody「元のファイルも削除され」は INV-009(物理非破壊)違反 → 文言を是正して採用。[[mock-ui-ir-is-cad]] でモックは CAD(原器)だが、原器でも安全制約(物理非破壊・S-26 L3 物理差分)は覆せない。設計時に「モックの破壊的文言 vs 実挙動」を必ず突き合わせる。
- lesson: 既存の完成済 Core(Restore/PermanentDelete)を再利用し、表面のみ作り直す(ECO-014/015 の型の再適用)。新規 Core を足さないことで INV-009 の再証明を不要にした。
