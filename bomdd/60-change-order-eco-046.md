# Change Order — ECO-046(implemented・golden 待ち): タグ削除×未保存ビュー階層編集の谷間 — 保存が FK 違反の未処理例外(「予期しないエラー」)

> ECO-045 golden ウォークスルー中に maintainer が発見した**別欠陥**(R3 分離起票)。
> ECO-045(使用中タグ定義の削除拒否)のガードは**保存済み参照(DB)**のみを見るため、
> 「未保存の編集状態にだけ載っているタグ」は使用中と判定できない — その谷間で保存がクラッシュ級エラーになる。

## 1. 症状(maintainer 実機 2026-07-05)

再現手順:

1. タグタブでビューの階層エディタにタグ X を配置(**未保存**・dirty)。
2. タグパレットから X を削除 → **削除成功**(DB 上は未使用のため ECO-045 ガードは通す=ガード自体は仕様どおり)。
3. 階層エディタを保存 → **「予期しないエラーが発生しました。詳細はログを確認してください。」**(error.unhandled=グローバル例外ハンドラ)。

## 2. 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **未定義 → 裁定要** | tag_tab.md は「編集中(未保存)の階層に置いたタグの定義が削除された場合」の挙動を未定義。TAG-008 裁定の「使用中」に**未保存の編集状態を含むか**も未定義(裁定資料 §2 の表は DB 参照 3 種のみ) |
| BOM | 無規定 | INV-008 は**読み取り**経路の参照切れ耐性のみ。書き込み経路(保存)の参照切れの扱いは spec/E-BOM に無規定 |
| 実装 | **欠陥(2 層・どの裁定でも 1 層目は要是正)** | §3 参照。少なくとも「Core が未処理例外を投げる」のは UX 裁定に依らず欠陥 |

## 3. 切り分け済みの事実

確定(実測・ファイル/行):

- **残置の仕組み**: `TagsTabViewModel.OnTagsChangedAsync`(src/ViewPrism2.App/ViewModels/TagsTabViewModel.cs:304)
  は `!Editor.IsDirty` のときだけエディタを再読込する(**意図的**=未保存編集の保護。コメントは
  CASCADE 後の最新化目的)。dirty のときは削除済みタグのノードがメモリ内ツリーに残る。
- **保存の無防備**: `ViewService.SaveHierarchyAsync`(src/ViewPrism2.Core/Services/ViewService.cs:280-328)
  の検証は view 存在・別ビュー混入・ノード id 重複・親存在・循環のみで、**node.TagId の存在を検証しない**。
  そのまま `ReplaceHierarchyAsync` が INSERT → `view_tag_hierarchies.tag_id REFERENCES tags(id)`
  (DatabaseSchema.cs:100)の FK 違反。
- **例外経路**: FK 違反例外は Result へ変換されず伝播 → グローバルハンドラの error.unhandled 表示。
- **ECO-045 起因ではない(潜伏欠陥の顕在化)**: 保存経路・dirty ガードとも ECO-045 で無改変。
  ECO-045 **以前**は保存済み配置を持つタグも削除できた(CASCADE)ため、同型の事故面はより広かった。
  ECO-045 で保存済み参照は削除拒否になり、**残った穴が「未保存編集状態」のみ**になった。

疑い(未検証・プローブで実測):

- 例外の具体型(SqliteException/FK violation)とログ内容は未採取。是正時のプローブ
  (「存在しないタグ id を含むノード集合で SaveHierarchyAsync → 現状は例外」)で実測する。

## 4. 是正方針(案・裁定後に確定)

どの案でも **Core 防御は共通実施**: `SaveHierarchyAsync` に tag_id 存在検証を追加し、
不存在は未処理例外でなく Result(ValidationError 系)で返す(書き込み経路の参照切れ耐性
= INV-008 の書き込み版)。UX の選択肢:

| 案 | 内容 | diff 規模 | 含意 |
|---|---|---|---|
| **U-a(推奨)** | **削除ガードの編集状態への外延**: タグタブの階層エディタ未保存ツリーに載っているタグの削除も拒否(TagInUse と同じ理由提示)。+Core 防御 | 中(パレット→エディタ状態の参照配線+Core 検証) | TAG-008「使用中は削除拒否」の精神に最も忠実。未保存でも「見えている使用」は保護される |
| U-b | 削除は許し、TagsChanged で dirty エディタからも当該ノードを除去(通知表示) | 中 | 編集中ツリーの一部が黙って(または通知付きで)消える=驚き。編集保護の意図と衝突 |
| U-c | 削除は許し、保存時に「削除済みタグの配置が含まれています」で保存拒否(手動除去を促す)。Core 防御のみで完結 | 小 | 実装最小だが、ユーザーは配置し直し/除去の手作業を強いられる |

## 5. 影響 BOM

- E-VIEWSVC-009(SaveHierarchyAsync 検証追加 — 全案共通)
- E-UI-NODEGRAPH-025(階層エディタ)/ E-UI-TAGS-026(タグパレット)— U-a/U-b の配線
- E-TAGSVC-008(U-a の場合の拒否理由整合)
- 41-fixed-oracle(新規行: 不存在 tag_id を含む保存の Result 化)・20-spec(書き込み経路の参照切れ規定)

## 6. 残ゲート

- ~~gate①(裁定)~~ → **受領(maintainer 2026-07-05): U-a 採用**。CAD 反映済み= TAG-008 裁定資料 §2 表へ
  「未保存の編集状態=対象」行追加+§4 裁定記録追補+tag_tab.md 明文(ViewPrismUI `d386471`)。
- **gate②(golden)**: §7 の合格基準参照。

## 7. 実施記録(2026-07-05・/eco-fix)

### プローブ(R5・是正前実測)

- **プローブ B(U-a 挙動)**: CpUiG6HierarchyEditorTests へ受入 2 件先置き →
  「未保存の階層編集に載っているタグの削除は拒否される」が **Assert.NotNull(GetByIdAsync)=null で不合格
  = 削除が成功してしまう**ことを実測(Tests 551 中 550 合格・1 不合格)。境界テスト
  (保存済み・非 dirty → DB ガード TagInUse)は是正前から合格= ECO-045 ガードの正常動作を確認。
- **プローブ A(Core 防御)**: CpView012HierarchySaveTests へ受入 1 件先置き →
  **CS1729(ViewService に ITagRepository 注入口が無い)を実測**(ECO-041/044 前例の API 不在プローブ)。

### 是正内容(U-a+Core 防御・diff)

| 層 | ファイル | 内容 |
|---|---|---|
| Core | ViewService.cs | ctor へ `ITagRepository? tags = null` optional 注入(V4 CHEAT-01 前例・既存 20+呼び出し互換)。SaveHierarchyAsync に参照切れ検証: 不存在 tag_id を含むと NotFound の Result(FK 例外の未処理伝播を根絶)・既存階層無傷 |
| App | HierarchyEditorViewModel.cs | `ContainsTag(tagId)` 公開(編集中ツリーの走査) |
| App | TagPaletteViewModel.cs | `IsTagInUnsavedEdit` フック+DeleteAsync 先頭で確認前拒否(error.tagInUnsavedEdit) |
| App | TagsTabViewModel.cs | 配線: `Palette.IsTagInUnsavedEdit = tagId => Editor.IsDirty && Editor.ContainsTag(tagId)` |
| App | App.axaml.cs / i18n ja+en | ViewService へ ITagRepository を DI 注入(production 必須)・拒否文言追加 |
| tests | CpUiG6HierarchyEditorTests / CpView012HierarchySaveTests | 受入 3 件(U-a 拒否+非 dirty 委譲+Core 防御) |
| Oracle | S39StaleTagHierarchySaveTests.cs | S-39 新設(参照切れ保存の拒否+無傷+現存のみ成功) |
| BOM | 10-requirements / 41 / 30-ebom / 20-spec | REQ-083 新設・S-39 行・E-VIEWSVC-009/E-UI-TAGS-026 invariant・§2.2 明文 |

DB スキーマ変更なし・62 不要・35-dsbom 不要。既存オラクル行無改変(R6・新規行のみ)。

### 機械受入(4 点・全緑)

- `dotnet build`: 0 error / 0 warning
- `dotnet test tests/ViewPrism2.Tests`: **552/552**(プローブ合格転化)
- `dotnet test tests/ViewPrism2.Oracle`: **102 合格+2 skip(既知)**(S-39 追加で 101→102。回帰ゼロ)
- `python bomdd/validate_bom.py`: 0 error / 0 warning

### golden 合格基準(gate②・maintainer 実機)

1. **§1 の再現手順の再走**: ビューにタグ X を配置(未保存)→ パレットから X を削除
   → **削除されず**「編集中のビュー階層に配置されているタグは削除できません。配置を外すか、
   編集をキャンセルしてください」が表示される(確認ダイアログは出ない)。ツリーの配置は無傷。
2. 続けて編集を**キャンセル**(配置を破棄)→ X を削除 → 削除成功。
3. 逆に配置を**保存**してから X を削除 → ECO-045 の拒否(「使用中のタグは削除できません…」)。
4. 回帰: タグを配置して保存 → 正常に保存できる(「保存しました」)。
