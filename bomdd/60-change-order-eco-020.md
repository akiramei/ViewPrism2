# ECO-020(ECO-α): 作業スペースドメイン + 永続化 + 作業タブ shell/サイドバー + 受け渡し結線

- **status**: implemented(製造済・未コミット — 実機 golden=maintainer ゲート)
- **検証**: App build 0 警告(XAML コンパイル+compiled binding OK)/ Tests 445(+6 CP-WORKSPACE-028)/ Oracle 74+2skip(S-01〜S-31 退行ゼロ)
- **type**: 新規ドメイン + 永続化(DB)+ 新タブ surface + 既存意味論更新(CAD モック由来)
- **baseline**: ECO-019 適用後(HEAD b2b9a8c 系)
- **bom_rev**: v4.0(eco:ECO-020)
- **cad_input**: `ViewPrismUI:資料/画像タブ/ViewPrism2 作業タブ.html`(取込前=maintainer Downloads)
- **UI-IR/UI-BOM**: `bomdd/ui/work-tab/`(ui-ir / ui-bom / ui-trace-map / extraction-report / unresolved-questions)
- **charter 整合**: 00-charter-v4 §裁定記録「ORB・**作業スペースは後続**」の番が来た(予定機能)。

## 0. なぜ分割するか(UQ-W05 決定)

作業タブは過去の画像タブ ECO 群(014/015/017/018/019=Core 意味論を再利用するだけ)と質が違い、**net-new Core(WorkspaceService/Repository)+ DB schema + 新タブ**を含む。よって2段分割:

- **ECO-α(本書 ECO-020)** = 作業スペースドメイン Core + 永続化(DB)+ 作業タブ shell + サイドバー + 受け渡し結線。**MVP=作業スペースに画像が集まり、選択・追加・リネーム・閲覧(グリッド/リスト・ソート)ができる。**
- **ECO-β(後続 ECO-021)** = 右ペイン surface(タグ編集 / 作業=別スペース移動 / 整理=類似+マージ / ⋯=修復・削除・ゴミ箱 popup)を作業タブへ再利用配線。

α で Core を固定オラクルで固め、β は surface 再利用を golden で回す(画像タブ M1→M4 と同じ規律)。

## 1. スコープ(ECO-α)

### 1.1 新規ドメイン
- **作業スペース(Workspace)**: 名前付き・永続・ユーザー管理の画像集合。属性 `id / name / is_default / seq / created_at`。
- **作業スペース所属(WorkspaceImage)**: workspace × image の多対多(集合)。

### 1.2 永続化(DB・UQ-W01 決定=DB テーブル)
`E-DB-010` を拡張。マイグレーションで2テーブル追加(sync_folders/images と同型):

```sql
CREATE TABLE workspaces (
    id          TEXT    NOT NULL PRIMARY KEY,
    name        TEXT    NOT NULL,
    is_default  INTEGER NOT NULL DEFAULT 0,
    seq         INTEGER NOT NULL,
    created_at  TEXT    NOT NULL
);
CREATE UNIQUE INDEX idx_workspaces_default ON workspaces(is_default) WHERE is_default = 1;  -- INV-W1 を DB で担保

CREATE TABLE workspace_images (
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    image_id     TEXT NOT NULL REFERENCES images(id)     ON DELETE CASCADE,
    added_at     TEXT NOT NULL,
    PRIMARY KEY (workspace_id, image_id)
);
CREATE INDEX idx_workspace_images_image ON workspace_images(image_id);
```
- `ON DELETE CASCADE`: 画像が完全削除(E-TRASH-038 PermanentDelete)されたら所属も消える(幽霊参照防止)。**ソフト削除(deleted)では所属は残し、件数/一覧から status で除外**(INV-W2・復元で戻る)。
- 部分 UNIQUE インデックスで「デフォルトは高々1つ」を DB レベルで担保(INV-W1)。

### 1.3 新規 Core サービス(E-WORKSPACE-042)
純粋度の高い決定論ロジック(状態遷移・回転・集合演算)を Core に置き unit 検査可能にする:

| 操作 | 意味 | 不変条件 |
|---|---|---|
| `EnsureDefaultExists(clock)` | 初回シードでデフォルト1件 | INV-W1 |
| `ListWorkspaces()` | 件数付き(normal のみ)・seq 順 | INV-W2 |
| `CreateWorkspaceRotatingDefault(clock)` | 新規空デフォルト+旧デフォルトを時刻名で is_default=false へ降格 | INV-W1(原子的に1つ) |
| `RenameWorkspace(id, name)` | 非デフォルトのみ・空は『スペース』へ | INV-W3 |
| `AddImagesToDefault(imageIds)` | **受け渡し**=デフォルトへ和集合追加 | INV-W2/W4 |
| `MoveImages(fromId, toId, imageIds)` | 元から除去+移動先へ和集合(原子) | INV-W4/W5(surface=β) |
| `GetWorkspaceImages(id)` | 所属画像(normal・安定順) | INV-W2 |

### 1.4 作業タブ shell + サイドバー(E-UI-WORKSPACE-043)
- ナビ第3タブ『作業』(E-UI-SHELL-021 拡張: SelectedTabIndex==2・`navigation.work` loc・`IsWorkTabSelected`・`ShowWorkTabCommand`・`WorkTabView`/`WorkTabViewModel`)。
- 左=作業スペースサイドバー(一覧/選択/＋追加=回転/リネーム/件数/276⇄64 折り畳み・rail)。
- 中央=ワークスペースヘッダ(名前+既定（自動追加先）+件数)+現スペースの画像をグリッド/リスト+ソート+タグ絞り込みチップ+空状態。**右ペイン文脈モードは ECO-β**(α では閲覧のみ)。

### 1.5 受け渡し結線(DOM-0026)
画像タブ作業モード『追加』(`ImageTabViewModel.AddToWork`・ECO-017)を、`_workTargets`(session List 行き止まり)から **`E-WORKSPACE-042.AddImagesToDefault` への書き込み**へ変更。ステータスバーに「N 枚を作業スペース『…』へ追加」を通知(UQ-W02 lean)。

## 2. 新規 REQ(提案・10-requirements.yaml へ追加要)
- **REQ-074**: 作業スペースは名前付き・永続の画像集合で、同時にデフォルトは厳密に1つ。『＋』はデフォルトを回転(新規空デフォルト作成+旧デフォルトを時刻名で保存済みへ降格)。デフォルトはリネーム/削除不可で、画像タブ作業モード『追加』の自動追加先。所属は集合(重複なし)、件数/一覧は status=normal のみ。
- **REQ-075**: 作業スペースへの追加/移動は所属(workspace_images)の論理操作のみで、画像行・物理ファイル・image_id・タグに触れない(INV-009/INV-001 整合)。移動は元除去+移動先和集合の原子操作。

## 3. 不変条件(新規 INV-W*)
- **INV-W1**: 同時にデフォルト作業スペースは厳密に1つ(DB 部分 UNIQUE + 回転で担保)。
- **INV-W2**: 所属は集合。件数/一覧は status=normal のみ(deleted/missing 除外・INV-010 整合)。
- **INV-W3**: デフォルトはリネーム/削除不可。
- **INV-W4**: add/move は workspace_images の論理操作のみ。物理非破壊(INV-009)・image_id 不変(INV-001)。
- **INV-W5**: 移動は元除去+移動先和集合の原子(失敗時ロールバック INV-006)。

## 4. 影響 BOM
- **E-WORKSPACE-042**(新規 core)= 作業スペースサービス。requirement_refs=[REQ-074, REQ-075]・depends_on=[E-DOMAIN-001, E-DB-010]。
- **E-UI-WORKSPACE-043**(新規 surface)= 作業タブ shell+サイドバー。external_source_ref=作業タブモック・depends_on=[E-WORKSPACE-042, E-UI-SHELL-021, E-UI-BROWSE-022]。
- **E-DB-010**(拡張)= workspaces / workspace_images テーブル + マイグレーション。
- **E-UI-SHELL-021**(拡張)= 第3タブ『作業』のナビ追加。
- **E-UI-MODE-041**(意味論更新)= ECO-017 invariant『workTargets セッション内蓄積のみ・永続化スコープ外』→ AddImagesToDefault 書き込みへ(受け渡し成立)。
- **E-DOMAIN-001**(拡張)= Workspace/WorkspaceImage エンティティ。
- bomdd/ui/work-tab/* 一式。

## 5. オラクル/検証計画
- 新 Core(E-WORKSPACE-042)= 固定フィクスチャで exact 検査(unit): デフォルト回転(常に1つ)・リネーム(デフォルト不可・空フォールバック)・add 和集合・move 原子・件数 status 除外。**固定オラクル S-33 候補(M4 で凍結検討)**。
- マイグレーション= 新規 DB=最新スキーマ+既適用マーク / 既存 DB=migration 適用で2テーブル増・既存データ不変(CP-DB-006 整合)。
- 受け渡し= 画像タブ AddToWork → デフォルトスペース件数増を unit 検査。
- 既存 S-01〜S-31 不変・スキーマは**追加のみ(後方互換)**。
- 実機 golden(maintainer ゲート)= 作業タブ表示・スペース選択/追加(回転で時刻名保存)/リネーム/件数・受け渡し(画像タブで追加→作業タブに出現)・空状態。

## 6. スコープ外(ECO-α)
- 右ペイン文脈モード一式(タグ編集/作業=別スペース移動/整理/⋯=修復・削除・ゴミ箱)=**ECO-β**。
- 整理(類似+マージ)の作業タブ配線(UQ-W03 決定=出すが β)。
- 作業スペースの削除 UI(モックに無し)・スペースの並べ替え・スペースへの手動 D&D 追加。
- 詳細/ノート(REQ-043・別 ECO)。

## 7. 決定記録
- **W-α1**: 永続化=DB テーブル(workspaces + workspace_images)。settings.json 不採用(UQ-W01 maintainer)。
- **W-α2**: ECO 段階分割 α/β(UQ-W05 maintainer)。
- **W-α3**: デフォルト回転=新規空デフォルト+旧を時刻名で降格。常にデフォルト1つ(INV-W1)。
- **W-α4**: 受け渡し=画像タブ AddToWork をデフォルトスペース書き込みへ(ECO-017 繰延の解消)。
- **W-α5**: ソフト削除では所属を残し status で除外(復元で戻る)・完全削除のみ CASCADE で所属除去。
- **W-α6**(β 予約): 整理を作業タブに出す(UQ-W03 maintainer)。
