# Change Order — ECO-045(implemented・golden 待ち): TAG-008 裁定の取り込み — 使用中タグ定義の削除は拒否(無効化/非推奨化は別操作)

> CAD review_points TAG-008(risk: high・未確定)の maintainer 裁定(2026-07-05・CAD `63d09bc`)の取り込み。
> ECO-042/043/044 と同型の「設計確定の取り込み」だが、本件は**現状実装(無条件削除+FK カスケード)と
> 衝突する挙動変更**であり、かつ**固定オラクル S-11 の前提(使用中タグの削除成功)と交差する**。
> 実装方式に R6 絡みの裁定分岐が残る(§4)。

## 1. 裁定(maintainer 2026-07-05・CAD `63d09bc`)

> 使用中タグ定義の削除は拒否。必要なら「無効化/非推奨化」を別操作にする

- 使用中のタグ定義は削除できない。削除操作は拒否し、理由を提示する。
- 無効化/非推奨化は削除とは別操作。**本 ECO では導入しない**(削除操作へ混ぜないことだけを確定。
  必要が生じたら分離起票)。
- REQ-028(FK カスケード)は「削除が実行された場合の DB 整合性規則」として存続
  (削除可否の判定はサービス層の責務・カスケードは防御層)。
- CAD 記録: review_points.md TAG-008 → 決定・`docs/screens/tag_tab.md` タグパレット節+
  インタラクション表へ明文・裁定資料 `docs/decisions/TAG-008-tag-definition-deletion.md`。

## 2. 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **欠陥だった → 本日確定済み** | TAG-008 が未確定(risk: high)のまま。tag_tab.md はタグパレットの削除アクションを定義するが使用中の挙動は未定義だった。裁定で確定・明文化済み(`63d09bc`) |
| BOM | **追随要** | REQ-028 は「カスケード規則」のみで削除可否は無規定(裁定と論理両立)。ただし固定オラクル S-11 のテスト実装が「使用中タグの service 層削除成功」を前提にしており折り合い方式の確定が必要(§4)。使用中削除拒否の REQ/受入行は不在 |
| 実装 | **裁定に不適合(仮置きの帰結・欠陥ではない)** | 未確定仕様の仮置きとして V1 から無条件削除で稼働。混入コミット特定は不適用(欠陥混入でなく仕様未確定) |

## 3. 切り分け済みの事実

確定(実測・ファイル/行):

- **Core にガードなし**: `TagService.DeleteAsync`(src/ViewPrism2.Core/Services/TagService.cs:95)は
  存在チェックのみで削除実行。波及は FK(REQ-028)。
- **UI は確認ダイアログのみ**: `TagPaletteViewModel.DeleteAsync`(src/ViewPrism2.App/ViewModels/
  TagPaletteViewModel.cs:182)。文言「この操作は取り消せません」(tag.deleteTagConfirmation)は
  使用中でも表示され削除が通る。
- **呼び出し元は 2 箇所のみ**: UI(TagPaletteViewModel.cs:193)+固定オラクル
  (tests/ViewPrism2.Oracle/S11TagDeletionRippleTests.cs:58)。
- **S-11 との交差**: S11TagDeletionRippleTests.cs:58-59 は「画像付与+ビュー条件+階層配置+
  子タグを持つタグ」を `TagService.DeleteAsync` で削除し `IsSuccess` を assert。Core にガードを
  入れると不合格になる。**ただし** 41-fixed-oracle.yaml の S-11 契約行(scenario/expectation)は
  削除の「層」を指定していない(「タグを削除し、当該ビューを開いて…構築・評価とも例外なし」)。
  契約の本質は INV-008(参照切れへの読み取り耐性)であり、削除拒否導入後も旧 DB・異常系で
  参照切れは起こり得るため、この検査意図は存続する。
- **使用中判定の材料**: `GetUsageCountsAsync`(ITagRepository.cs:54・REQ-029)は
  image_tags(COUNT DISTINCT image_id)のみ。ビュー階層(view_tag_hierarchies)・
  条件(view_conditions)の参照判定クエリは不在 → 追加が必要。
- **「無効化/非推奨化」の概念は DB/Core/UI のどこにも不在**(net-new だが本 ECO スコープ外)。

疑い(未検証):

- なし(本件は裁定取り込みであり真因調査対象の欠陥ではない)。

## 4. 是正方針(案・着手時確定)— 裁定分岐 2 点

### 4a. 「使用中」の範囲(CAD 裁定資料 §2 の提案の確認)

| 参照 | 拒否対象(提案) |
|---|---|
| 画像への値付与(image_tags) | **対象** |
| ビュー階層への配置(view_tag_hierarchies) | **対象** |
| ビュー条件からの参照(view_conditions) | **対象** |
| 子タグの親(tags.parent_id) | **対象外**(定義側の階層編成=「使用」でない。ルート化 SET NULL 存続) |

### 4b. 固定オラクル S-11 との折り合い(R6)

| 案 | 内容 | diff 規模 | R6/含意 |
|---|---|---|---|
| **O-a オラクル入口変更(推奨)** | Core `TagService.DeleteAsync` に使用中ガード(拒否= ValidationError)。S-11 テスト実装の削除入口 1 行を repository 直呼び(`db.Tags.DeleteAsync`)へ変更。41 yaml の S-11 契約行は**無改変**(INV-008 読み取り耐性の検査意図は完全保存)。拒否の受入は新規行で追加 | 小(ガード+参照判定クエリ+テスト 1 行+新規行) | 強制が Core にあり裁定に忠実。**テストファイルの字面変更を伴うため R6 解釈の裁定必須**(yaml 契約行は不変・検査意図も不変、という整理で例外承認を求める) |
| O-b optional 拡張(Oracle 完全無改変) | `TagService.DeleteAsync` の意味論は現状のまま。使用中判定+拒否は UI 経路(または新 API)に実装 | 小 | R6 完全遵守。ただし Core に「使用中でも消せる」API が残る=裁定の強制が Core にない(ECO-041/044 で否定した見せかけ構造の変種)。**非推奨** |
| O-c S-11 廃止+置換 | S-11 を廃止扱いにし拒否検査+repository 層波及検査の新規 2 行で置換 | 中 | R6 の正面例外(行の廃止)。検査意図は O-a で保存できるため過剰。**非推奨** |

### 是正内容(O-a 前提の見取り図・確定は /eco-fix)

1. `ITagRepository` へ使用中判定(view_tag_hierarchies/view_conditions の参照有無。image_tags は
   既存 GetUsageCountsAsync 流用可)を追加。
2. `TagService.DeleteAsync` に使用中ガード → `ValidationError`(理由文言に参照種別)。
3. UI: 拒否時 StatusMessage 表示(既存 ErrorMessages 経路)+i18n 文言追加。
   確認ダイアログ文言「取り消せません」は未使用タグ限定になるため整合を確認。
4. プローブ先行(R5): 是正前に「使用中タグの削除が成功してしまう」ことを実測する不合格テストを先置き。
5. 受入新規行: 「使用中(付与/配置/条件)タグの削除拒否+拒否時無傷」「未使用タグの削除成功」。
   既存オラクル行は無改変(S-11 は入口 1 行のみ・4b 裁定後)。

DB スキーマ変更なし(62 不要)・35-dsbom 不要(surface 新設なし)。
golden: UI 拒否挙動(使用中タグで削除 → 拒否理由表示・タグ無傷/未使用タグで削除成功)は要実機確認。

## 5. 影響 BOM

- E-TAGSVC-008(使用中削除拒否の invariant 追加・since ECO-045)
- 10-requirements(削除可否の REQ 新設 or REQ-028 系の増補 — 着手時確定)
- 41-fixed-oracle(新規行追加。S-11 は 4b 裁定に従う)
- M-UI-013(TagEditor/パレット系 view)・タグタブ UI 品目(拒否メッセージ表示)
- 20-spec §2.2/§2.6(削除可否の明文)

## 6. 残ゲート

- ~~gate①(裁定・残 2 点)~~ → **受領(maintainer 2026-07-05): 4a=提案どおり・4b=O-a**。
- **gate②(golden)**: 是正後の実機確認(使用中拒否+未使用削除成功)— §7 の合格基準参照。

## 7. 実施記録(2026-07-05・/eco-fix)

### 裁定確定(gate①)

- **4a「使用中」の範囲=提案どおり**: 付与(image_tags)・配置(view_tag_hierarchies)・
  条件参照(view_conditions)は拒否対象。子タグの親(tags.parent_id)は対象外(ルート化 SET NULL 存続)。
- **4b= O-a(オラクル入口変更)**: Core に強制・S-11/CP カスケードテストの削除入口のみ
  repository 直呼び化。41 yaml の S-11 契約行は無改変(INV-008 読み取り耐性の検査意図は存続)。

### プローブ(R5・是正前実測)

- CpTag011Tests へ受入 5 件を先置きし実行 → **拒否系 3 件が不合格**
  (`画像に付与済み…`= IsSuccess が True / `ビュー階層に配置済み…`・`ビュー条件から参照…`= Error が
  TagInUse でなく null)・境界 2 件(子ルート化・未使用削除)は合格 = 真因「TagService.DeleteAsync に
  ガード不在」を実測で裏取り。Tests 549 中 546 合格・3 不合格(是正前)。

### 是正内容(diff)

| 層 | ファイル | 内容 |
|---|---|---|
| Core | ErrorCode.cs | `TagInUse` 追加(enum のみ・挙動不変) |
| Core | ITagRepository.cs | `GetUsageRefsAsync(tagId)`+`TagUsageRefs`(3 参照カウント・InUse)追加 |
| Infrastructure | TagRepository.cs | 3 スカラーサブクエリ 1 発の実装(dynamic 読み+Convert= ECO-002 DF-2 と同じ型親和性防御) |
| Core | TagService.cs:DeleteAsync | 使用中ガード(InUse → TagInUse 拒否)。カスケード注記を防御層へ改記 |
| App | ErrorMessages.cs / i18n ja+en | `error.tagInUse` 追加(UI 拒否理由表示は既存 StatusMessage 経路) |
| tests | CpTag011Tests.cs | 受入 5 件追加+既存カスケードテストの削除入口を repository 直呼び化(O-a) |
| Oracle | S11TagDeletionRippleTests.cs | 削除入口 1 行のみ repository 直呼び化(O-a・契約/期待値は無改変) |
| Oracle | S38TagInUseDeletionGuardTests.cs | S-38 新設(3 種拒否+無傷・子ルート化・未使用削除成功) |
| BOM | 10-requirements / 41-fixed-oracle / 30-ebom / 20-spec | REQ-082 新設・S-38 行追加・E-TAGSVC-008 invariant+refs・§2.2 明文 |

UI の確認ダイアログ(「取り消せません」)は未使用タグ限定の到達になり文言整合(変更なし)。
DB スキーマ変更なし・62 不要・35-dsbom 不要。

### 機械受入(4 点・全緑)

- `dotnet build`: 0 error / 0 warning
- `dotnet test tests/ViewPrism2.Tests`: **549/549**(プローブ 3 件が合格に転化)
- `dotnet test tests/ViewPrism2.Oracle`: **101 合格+2 skip(既知)**(S-38 追加で 100→101。S-01〜37 回帰ゼロ)
- `python bomdd/validate_bom.py`: 0 error / 0 warning

### golden 合格基準(gate②・maintainer 実機)

1. タグタブのタグパレットで、**画像に付与済みのタグ**の削除を実行(確認ダイアログ → 削除)
   → 削除されず、拒否理由「使用中のタグは削除できません…」が表示される。タグ・付与とも無傷。
2. **ビューの階層に配置しただけのタグ**(付与なし)で同様 → 拒否・配置無傷。
3. **未使用のタグ**で同様 → 削除成功(一覧から消える)。
4. ~~子タグを持つだけの未使用親タグで同様~~ → **実機対象外に修正(2026-07-05)**:
   タグ定義の階層(tags.parent_id)を組む UI が存在しないことが golden 準備で判明
   (maintainer 指摘 → 実測確認。51-cheat-log R3 記録参照)。当該境界(子タグの親は「使用」でない)は
   S-38+CP-TAG-011 の機械受入で担保済み。
