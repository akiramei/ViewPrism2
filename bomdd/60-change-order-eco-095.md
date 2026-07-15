# ECO-095 — デフォルト作業スペース名の多言語不追随 — UI ロケール文字列の永続データ焼き込み(Core シード)

- type: 不具合(i18n 漏れ・第 3 の様態= Core 層シードデータへのロケール文字列焼き込み)
- status: staged
- baseline: main 8e866a9
- 起票日: 2026-07-15
- 発見: ECO-094 gate②(golden)中の maintainer 実機所見(en 切替検査)。**ECO-094 とは別サーフェス・
  別欠陥のため R3 分離起票**(チップ行は無関係・混入は作業タブ初期実装から)。

## 1. 症状

言語を en へ切り替えたとき、作業タブ サイドバーのデフォルト作業スペース行が
「デフォルト」(日本語)のまま残る。同じ行の既定バッジは「Default (auto-add target)」へ
正しく追随する(workspace.defaultAutoAdd キーは ja/en 完備)— 名前だけが取り残される。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **沈黙**(mock は ja のみ・シード名のロケール挙動は未定義) | i18n はモックが沈黙するアプリケーションスコープの横断規約(K-BOM REQ-050/051・ECO-079/080 の枠組み)— モックに書いていない≠規約がない |
| BOM | 健全 | REQ-050/051(文言直書き禁止)は宣言済み。検査(i18n 走査)の適用面に谷間(§3) |
| 実装 | **欠陥と確定** | `WorkspaceService.cs:17` `public const string DefaultName = "デフォルト"` — Core 層の定数が **DB シード名として永続化**される(§3 実測) |

## 3. 切り分け済みの事実(確定・8e866a9 時点)

1. **焼き込み経路(実測)**: `WorkspaceService.DefaultName = "デフォルト"`(Core・:17)→
   `EnsureDefaultExistsAsync`(:40)と**デフォルト回転** `CreateRotatingDefaultAsync`(:77)の
   両方が `Name = DefaultName` で DB へ保存。表示は DB の `Workspace.Name` をそのまま
   (サイドバー行= WorkTabViewModel.cs:378・ヘッダー= :400 `WsName`)。ロケール切替は DB 値に
   作用しない=不追随は構造的。
2. **バッジは別経路で健全**: workspace.defaultAutoAdd(ja=「既定（自動追加先）」/en="Default
   (auto-add target)")は Loc 経由=追随する。症状の「名前だけ残る」と整合。
3. **混入**: `f211fa9`(ECO-020/021=作業タブ初期実装)から。潜伏機序= ja 環境では正しい表示のため
   機能等価で潜伏・en 切替検査(ECO-094 golden 基準 6)が初可視化。
4. **検査の谷間**: ECO-079 の i18n 全数走査は XAML 直書き+VM 算出文言の 2 層 — **Core 層が
   シードする永続データへのロケール文字列焼き込み**は検査次元に含まれていなかった(第 3 の様態。
   ECO-080 control-plan「暗黙前提 3 様態」への追補候補)。
5. **是正を単純化する既存制約**: デフォルト作業スペースは**改名不可**(サービス層で非デフォルトを
   検証済み= IWorkspaceRepository.cs:25 前提)・回転で降格した旧デフォルトは**時刻名へ改名**される
   (:37)。つまり is_default=1 の行の Name は事実上「デフォルト」固定=**表示専用化しても
   ユーザー編集と衝突しない**。
6. **副所見(スコープ外・51-cheat-log 記録)**: i18n キー `image.feature.defaultWorkspace`
   (ja/en とも定義済み)は**全ソース未使用**=デッドキー。i18n lint が未使用キーを検出しない谷間
   (ECO-091 R3 の重複キー所見と同族)。

### 未検証(疑い)

- Name を表示する他の面(移動先メニュー・受け渡し確認・エクスポート等)の全数は fix 時に走査
  (表示解決を採る場合は全面で一貫させる必要がある)。

## 4. 是正方針(案・gate① 裁定対象)

- **案A(推奨): 表示時解決** — is_default=1 の行は DB Name を使わず Loc(common.default 級)で
  表示する。DB スキーマ・既存データ不変・切替に即追随・既存 DB も直る。§3-5 の既存制約
  (デフォルト改名不可)により DB 名と表示名の乖離は実害なし。適用面= Name を表示する全面
  (fix 時に全数走査して一貫)。
- **案B: シード名の変更** — ロケール名または中立名("Default")でシード。既存 DB は直らない・
  シード後の切替に追随しない=症状が残る。劣後。

## 5. 影響 BOM(案A の場合)

- src= WorkTabViewModel(サイドバー行/ヘッダーの表示解決)+ Name 表示面の全数(fix 時確定)。
  Core(WorkspaceService)は不変(DB 名は識別子として残置)。
- テスト= en 切替でデフォルト行が "Default" 表示になる probe(是正前赤)+ ja 不変 pin。
- CP= i18n 検査(ECO-080)へ「永続データへのロケール焼き込み」次元の追補。
- i18n= 既存 common.default(ja=デフォルト/en=Default)を再利用可=キー新設なしの見込み。
- CAD= 変更なし見込み(ja 視覚不変)。

## 6. 残ゲート(2026-07-15 gate① 後更新)

- ~~gate①(裁定)~~ = **済**(§7)。
- **gate②(golden)**: 是正後、ja/en 切替の実機確認。

## 7. 裁定記録(2026-07-15 maintainer)

**gate① 裁定=案A 採択**(表示時解決)。is_default=1 の行は DB Name を使わず
Loc(common.default)で表示する。DB スキーマ・既存データ不変・切替に即追随・既存 DB も直る。
fix は Name 表示面の全数走査で一貫させる(/eco-fix eco-095)。

## 8. 実施記録(2026-07-15 /eco-fix)

**全数走査(表示面の確定)**: `Workspace.Name` の表示消費は WorkTabViewModel の 3 箇所のみ
(grep 実測): ①サイドバー行(:378)②中央ヘッダー WsName(:400)③移動先メニュー(:649)。
ImageTabWorkViewModel/MainWindowViewModel はサービス注入のみで名前を表示しない。
追加の実測= **文化切替ハンドラは行 VM を再構築していなかった**(GF-079-01 の再構築対象は
ソート列のみ)— 案A は構築時解決のため、切替→再射影の配線が症状是正に内在する
(同じ再構築で baked Sub ラベル〈自動追加先/保存済み〉の不追随も直る=スコープ内の帰結)。

**プローブ先行(R5)**: `CpI18n095DefaultWorkspaceNameTests` 新設(4 本)→ 是正前
**赤 3/3**(en 初期化・切替追随・移動先)+ ja pin 緑=真因(DB 焼き込み名の直接表示+
再射影なし)の実測裏取り。

**是正(案A)**: WorkTabViewModel のみ(**Core/WorkspaceService は不変**=DB 名は識別子残置):
1. `ResolveWorkspaceDisplayName(Workspace)` — is_default なら Loc(common.default)・他は DB 名。
2. 行構築を `RebuildWorkspaceRows()` へ抽出(ReloadWorkspacesAsync と言語切替の共通経路)+
   `_wsList` キャッシュ。3 表示面すべてに解決を適用。
3. 文化切替ハンドラへ同期再射影(行+WsName+移動先。DB 再読込なし・選択/リネーム状態不変)。
i18n= common.default(ja/en 既存)再利用=**キー新設なし**。

**機械受入**: build 0/0・**Tests 750/750**(probe 3 本赤→緑転+既存 746 無改変緑)・
Oracle 109+2skip(R6 不変)・validate_bom 0/0。

**セルフゴールデン(R7)**: XAML 無変更・VM 表示派生のみ。ja= 視覚不変(probe pin=「デフォルト」
表示・CAD mock は ja 正典と一致)。en= 名前が "Default" へ変化(=是正意図そのもの・バッジ
"Default (auto-add target)" と整合)。転写漏れ 0。

## 9. クローズ(gate② 待ち)

golden 合格後に /eco-accept eco-095(CP への「永続データ焼き込み」次元追補は accept 時 M4)。
