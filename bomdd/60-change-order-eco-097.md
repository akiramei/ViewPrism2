# Change Order — ECO-097(起票・staged): フィルタ変更後も非表示画像の選択が残り操作対象になる

> BL-002 の分離起票。2026-06-29 の外部レビュー所見を、2026-07-16 の maintainer 指示
> `/eco-file BL-002 フィルタ変更時に非表示画像の選択が残る` で正式な工程診断へ移した。

## 1. 症状(報告・2026-07-16)

- 画像タブの選択許可モード(タグ編集・作業・削除)で複数画像を選択し、FS 軸のタグチップで
  絞り込むと、一覧から消えた画像も内部選択 (`_selected`) に残る。
- そのまま操作すると、画面に見えていない画像も次の対象へ含まれる:
  - タグ編集: タグ付与・解除・数値連番。
  - 作業: デフォルト作業スペースへの追加。
  - 削除: normal → deleted のゴミ箱移動(物理ファイルは削除せず復元可能)。
- 再現手順:
  1. 同一コレクションに、タグ T を持つ画像 A と持たない画像 B を用意する。
  2. 画像タブでタグ編集・作業・削除のいずれかへ入り、A/B を複数選択する。
  3. タグ T のチップを押して A のみに絞り込む。
  4. B は一覧から消えるが選択件数・操作対象には残り、実行時に B も処理される。
- 発見元 BL-002 は 2026-06-29 に P2 所見として記録済み。今回の起票では新たな実機 golden は
  行わず、現行コード・履歴・既存テストで経路を再確認した。

## 2. 工程診断 — CAD/BOM の選択寿命契約が未定義(gate①が先)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **未定義・曖昧** | `docs/screens/image_tab.md` は単数/複数選択、FS 軸タグチップの絞り込み、各選択許可モードを定義するが、フィルタ変更時の選択を「維持/可視集合と交差/全クリア」のどれにするかを定義しない。VC-IMG-9 の「幅変更で選択・ナビ状態は変化しない」は overflow レイアウト再計算の契約であり、フィルタ変更時の画像選択寿命とは別。 |
| BOM | **契約欠落** | REQ-041、E-UI-BROWSE-022、E-UI-MODE-041、M-UI-IMAGETAB-035 は選択機構と操作先を宣言するが、絞り込みで母集合が縮むときの選択集合の遷移を宣言しない。CP-UI-G1/CpUiG1ImageTabSelectionTests にも同ベクタがない。 |
| 実装 | **暗黙に「維持」を実装** | `ImageTabViewModel.ClickChip` は `_tagFilter` を更新して `Recompute()` するだけで `_selected` を更新しない。後続操作は可視 Items でなく `_selected`/`SelectedIds` を直接消費するため、非表示画像も対象になる。CAD/BOM が未定義なので、裁定前に実装逸脱とは断定しない。 |

- 主因は **選択寿命(selection lifetime)という CAD/BOM の沈黙次元**。したがってコードから是正へ
  入らず、ViewPrismUI で方針を裁定し、CAD→BOM→実装の順で進める。
- 未確定事項との関係:
  - IMG-006(FS 軸タグチップの単一選択/複数 AND/OR)はフィルタ条件の**個数と結合規則**であり、
    本件の「フィルタ適用後に画像選択をどうするか」とは独立。IMG-006 を先に解決する必要はない。
  - IMG-023A/VC-IMG-9 の「幅変更で選択・ナビ状態不変」はチップの overflow 再配置に限る。
  - FL-*/VE-* に本件を既決する裁定は見つからない。

## 3. 切り分け済みの事実

### 3.1 現行画像タブ

- `ImageTabViewModel.cs:1868-1879`: view 軸のナビチップは `_selected.Clear()` してから潜る一方、
  FS 軸の絞り込みチップは `_tagFilter` 更新後に `Recompute()` するだけで選択を保持する。
- 画像タブ M3a 初版 `6f7b4f9`(2026-06-18)でこの挙動が導入され、以後継続。
- 非表示画像まで消費する経路はコードで確定:
  - `ApplySequential` / `ApplyTagAsync` / `RemoveCurrentTag`: `_selected` または `SelectedIds` を消費。
  - `AddToWork`: `_selected.ToList()` を作業スペースへ渡す。
  - `DeleteToTrash`: `_selected.ToList()` を Trash VM へ渡す。
- 物理画像を削除する欠陥ではない(INV-009 は維持)。ただし、見えない対象へのメタデータ変更・
  作業スペース追加・ソフト削除は誤操作の発見を遅らせる。

### 3.2 作業タブの安全先例

- `WorkTabViewModel.cs:1449-1459` はフィルタ適用時に `_selected` を新しい可視 ID 集合との
  交差へ縮退させる。クリア時は残存選択を維持し、落とした非表示選択を復活させない。
- `CpUiG1WorkTabTests.cs:327-344` が「絞り込みで非表示になった選択は落ちる」を固定済み。
- この安全側実装は `f211fa9`(2026-06-29・ECO-020/021)で、BL-002 発見時に画像タブへ
  ついで適用せず作業タブだけへ導入された(R3/既存 golden 保護)。

### 3.3 未検証(是正着手時のプローブ対象)

- 画像タブで A/B 選択→T 絞り込み→各操作を行う headless/VM プローブは未作成。
  `/eco-fix` ではまず是正前に不合格となるプローブで、非表示 B が選択・操作対象に残ることを実測する。
- タグ編集・作業・削除の3モードが同一 `_selected` を共有するため同じ真因と読めるが、受入では
  3操作を read-across して個別に確認する。

## 4. 是正方針の選択肢(gate①)

| 案 | 契約 | 製品 diff / golden 影響 | 評価 |
|---|---|---|---|
| **A: 可視集合との交差(推奨)** | フィルタ適用後 `selection := selection ∩ visibleIds`。フィルタ解除では残存選択を維持し、落とした選択は復活させない。 | `ImageTabViewModel.ClickChip` 近傍+回帰テスト。XAML/i18n/DB 不変見込み。画像タブのタグ編集・作業・削除で golden。WorkTab と安全契約が一致。 | 見えている選択を保持しつつ、非表示対象だけを排除する。既存先例を再利用でき最小。 |
| B: 全クリア | フィルタ値の適用/解除ごとに選択を全消去する。 | ImageTab は小 diff だが、WorkTab も統一するなら2面変更+両面 golden。統一しない場合は面間差を CAD に明記。 | 最も単純で安全だが、可視のままの選択も失い連続作業性が低い。 |
| C: 維持を正式化 | 選択は表示フィルタと独立し、非表示画像も操作対象に残す。 | src 変更なし、CAD/BOM/CP の doc-only 改訂。現挙動を golden で追認。必要なら非表示選択数/確認 UI は別の新設計。 | 操作対象が画面から確認できない安全上の弱点を残し、WorkTab とも不一致。非推奨。 |

## 5. 影響 BOM(裁定後に同期)

- CAD: `ViewPrismUI/docs/screens/image_tab.md` の選択/タグチップ/文脈モードへ選択寿命を追加。
  案 B/C で面差が残る場合は `work_tab.md` と共通意味論も明記。
- 要求/仕様: REQ-041 または画像タブ選択契約、`20-spec.md` §2.6。
- E-BOM: E-UI-BROWSE-022(選択集合)、E-UI-MODE-041(選択を消費する3モード)。
- M-BOM: M-UI-IMAGETAB-035(`ImageTabViewModel.ClickChip/Recompute` 境界)。
- Control Plan: CP-UI-G1。機械 fixture は `CpUiG1ImageTabSelectionTests.cs`、WorkTab パリティは
  `CpUiG1WorkTabTests.cs` の既存ベクタを維持。
- src/tests(案A予測): `ImageTabViewModel.cs` + `CpUiG1ImageTabSelectionTests.cs`。
- CAD 裁定だけでは固定 Oracle 行を変更しない(R6)。DB/schema、i18n、XAML/style は変更なし見込み。

## 6. 残ゲート

1. **gate①(現在地)**: maintainer が ViewPrismUI で案 A/B/C を裁定し、選択寿命を CAD 正典へ記録する。
2. 裁定後に `/eco-fix eco-097`: プローブ先行→BOM同期→最小実装→機械受入→R7 セルフゴールデン。
3. gate②: maintainer 実機で、タグ編集・作業・削除それぞれについて「非表示画像が意図せず
   操作されない/裁定どおり扱われる」ことと、フィルタ解除時の選択挙動を確認する。
4. `/eco-accept eco-097` で register applied、CP 観点、本文クローズ節を同期する。
