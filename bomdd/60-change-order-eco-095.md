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

## 6. 残ゲート

- **gate①(裁定)**: 案A/案B の選択(推奨=案A)。
- **gate②(golden)**: 是正後、ja/en 切替の実機確認。
