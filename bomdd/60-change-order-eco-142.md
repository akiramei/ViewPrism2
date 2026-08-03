# ECO-142 — コレクション切替が missing 母集合規模で遅延(事象カウントの駆動側選択)

- 種別: 不具合(実装層・性能特性・**実測済み**。件数の挙動は仕様どおり)
- status: **applied**(2026-08-03 起票 → 同日 fix `55c491d` → 同日クローズ。出典= maintainer 報告「6 件の類似コレクションが 3 桁コレクションより表示が遅い」)
- baseline: main `79123df`
- 優先度: 中(体感遅延は実データ依存。等価書き換えの実測裏取り済みで軽量クローズ見込み)

## §1 症状

コレクション切替(画像タブ)の所要時間が、**画面に見える件数ではなく status='missing' 母集合の規模**に
比例する。maintainer 実機の類似コレクション(normal 6 / missing 262,045)で、3 桁 normal のコレクション
(画像 354・スクリーンショット 204)より切替が体感で遅い。

実測(2026-08-03・実 DB 366MB を read-only CLI で計測・warm cache):

| 経路 | 類似(missing 262,045) | game capture(missing 0) |
|---|---|---|
| `CountIntegrityReviewEventsAsync` 現行 SQL | **318ms** | 2ms |
| 素の missing COUNT(covering index)= 物理下限 | 27ms | — |
| 意味論等価の駆動側反転版(§3-4) | **27ms** | — |

呼び出し元は [ImageTabViewModel.LoadContentAsync](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs#L436)
(切替経路で同期 await)および [ReloadImagesAsync](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs#L2935)。
warm で 318ms、アプリ起動直後の cold cache ではテーブル本体ページのランダム IO のため
さらに拡大する見込み(§3 未検証)。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD | 健全 | integrity_review.md の事象数(要確認の画像バッジ母数)意味論は正しく、性能規定は CAD の管轄外。missing をカウントに含めるのは仕様どおり(CMP-010 事象合計)— **除外は仕様違反になるため是正手段にならない** |
| BOM(検査封止) | 部分欠落 | CP-INTEGRITY-036 に事象カウント経路の計算量観点がない(26万件経路の系譜= ECO-104 全列挙・ECO-125 Recompute 台帳・ECO-134 計算量封止・ECO-137 走査回数の次、**N=8 例目候補**) |
| 実装 | **逸脱(真因)** | 結果(件数)は正。[ImageRepository.CountIntegrityReviewEventsAsync](../src/ViewPrism2.Infrastructure/Database/ImageRepository.cs#L307) の SQL が NOT EXISTS 相関サブクエリを **missing 側駆動**で書いており、計算量とページアクセスの形だけが欠陥 |

## §3 切り分け済みの事実

### 確定(実 DB・実行計画・git 実測)

1. **混入コミット**= `fb5173c`(ECO-140 fix・2026-07-24)。`git log -S "CountIntegrityReviewEventsAsync"`
   で単独ヒット。潜伏約 10 日。**マスキング要因**= 受入 TempDb の事象ベクタは小規模母集団で、
   計算量の差が観測不能。大規模 missing 残留は実機の類似コレクションにのみ存在した
   (過去に game capture 相当の内容が同 folder id で登録→パス変更で 262,045 行が missing 残留。
   relative_path のサンプルが game capture ライブラリの内容と一致・件数も 262,046 とほぼ一致)。
   **missing の大量残留自体はあり得る運用状態であり、データ整理は本 ECO のスコープ外**。
2. **実行計画(EXPLAIN QUERY PLAN 実測)**: missing 側を `idx_images_candidate_link` で SEARCH し、
   **1 行ごとに** ①相関サブクエリ(候補 pending の探索)②`m.id`/`m.hash` 取得のための
   テーブル本体ページ取得(hash はどのインデックスにも含まれない)が走る。
   262,045 行×(プローブ+ランダムページ取得)= 318ms(warm)の内訳。
3. **物理下限**: `idx_images_folder_status`(sync_folder_id, status)は素の COUNT に対して
   covering で、262,045 件の順次スキャンが 27ms。26 万件を数えること自体は遅くない。
4. **意味論等価の駆動側反転が成立**: 「候補が無い missing 数」=「missing 総数 −候補が有る missing 数」。
   候補(pending_origin='new' かつ candidate_link_id・hash 一致・タグなし)は pending 行からしか
   生まれないため、第 2 項は **pending 駆動**(件数は pending 規模)で数えられる。
   `COUNT(DISTINCT m.id)` で複数 pending が同一 missing を指すケースも同値。
   実測: 類似で 262,045(現行と一致)・27ms。非自明ケース(pending 9/missing 10 を併せ持つ
   別コレクション)で現行 11 =反転版 11 の結果一致を確認。
5. 切替経路の他クエリ(normal/pending 母集合・タグ join・trash count)はいずれも
   インデックスが効き missing 規模に比例しない(実測でシロ)。サムネイルもキャッシュ命中でシロ。

### 未検証(疑い — /eco-fix の最初に実測)

- **cold cache の実測差**(起動直後の初回切替。warm 318ms はランダム IO 分の下限)。
- アプリ経由(Dapper/Microsoft.Data.Sqlite)での反転版の実測(CLI と同等見込み)。
- 反転版の SQLite クエリプランが意図どおり(相関サブクエリの駆動が pending 側)であることの
  プラン実測(CLI では確認済み・アプリ接続でも同一のはず)。

## §4 是正方針(案・着手時確定)

- **案A(推奨)**: `CountIntegrityReviewEventsAsync` の SQL を駆動側反転
  (pending COUNT + missing COUNT − 候補有り missing COUNT〔pending 駆動・DISTINCT m.id〕)へ
  置換。意味論不変・§3-4 で実測裏取り済み。diff は当該メソッドの SQL 文字列のみ。
- **プローブ先行(必須・R5)**:
  1. **挙動同値プローブ**: 事象カウントのベクタ(pending 単独/missing 単独/候補有り一意組/
     曖昧組=複数 pending が同一 missing/タグ付き候補の除外/hash 不一致候補)で是正前後の
     件数 bit 一致を pin(CP-INTEGRITY-036 の既存事象ベクタと同族の計数版)。
  2. **計算量プローブ**: 固定時間閾値なしの原則に従い、(a)合成大規模 missing 母集団での
     係数反転(ECO-075 歴代承認の粗い上限型= O(M×N) 検出用)か、(b)プラン形状 pin
     (相関スキャンの駆動側)のいずれかを /eco-fix 冒頭で選定。是正前に不合格となる形を先に実測。
- **検査封止(併修)**: CP-INTEGRITY-036 へ「事象カウント経路の計算量= missing 母集合規模の
  相関プローブ×行取得を許さない」観点を追加(26万件経路 N=8 の台帳記帳)。

## §5 影響 BOM

- `src=` [ImageRepository.cs](../src/ViewPrism2.Infrastructure/Database/ImageRepository.cs#L307)
  (`CountIntegrityReviewEventsAsync` の SQL のみ・シグネチャ/意味論不変)
- `tests=` 事象カウント同値ベクタ+計算量プローブ(CpIntegrityReviewTests への追補または新設)
- `bomdd/33-control-plan.yaml=` CP-INTEGRITY-036 へ計算量観点
- `CAD=` 該当なし(視覚・挙動変更なし)

## §6 残ゲート

- **gate①(裁定)= 不要見込み**(実装層・件数 bit 一致・視覚変更なし)。
  ただし §3 未検証で反転版に意味論差が見つかった場合は軽微裁定へ切替。
- **gate②(golden)= 不要見込み**(挙動 bit 一致+視覚変更なし= ECO-134/137 先例どおり
  機械受入+同値プローブがクローズ条件の想定)。
- 着手条件: なし(プローブ先行のみ)。
- 関連= ECO-140(混入元・CMP-010 事象合計の意味論典拠)・ECO-134/137(計算量・走査回数封止の同型)・
  ECO-104/125(26万件経路台帳・N=8 例目候補)・ECO-136(missing 母集合の意味論)。
- R3 分離= 大規模 missing で「要確認の画像」一覧を開いた場合の全行 materialize
  (`GetIntegrityReviewByFolderAsync`)は本 ECO に混ぜない(51-cheat-log へ記録)。

## §7 実施記録(fix)

- **プローブ先行(R5)**: `CpIntegrityReviewTests` へ 2 本追加。
  1. **plan 形状 pin**(`R5_事象カウントはmissing母集合への相関プローブなしのcoveringカウントで実行される`):
     production 原文から SQL を抽出し TempDb で `EXPLAIN QUERY PLAN`。①相関 candidate プローブ署名
     `(sync_folder_id=? AND status=? AND candidate_link_id=?)` の不在 ②bulk カウント 2 本の
     covering index 化(`USING COVERING INDEX idx_images_*` ≥ 2)を assert。
     **是正前は ① が Sub-string found で不合格**(974 中 1 fail)= 真因の実測裏取り。
     固定時間閾値なし。
  2. **計数同値ベクタ**(`事象カウントは曖昧組を重複計上せず失格候補のmissingを数える`):
     missing 単独/移動一意組/曖昧組(1 missing: 2 new= DISTINCT 意味論)/タグ付き候補失格/
     hash 不一致失格/origin≠new 失格 → 10 件。**是正前から合格**= 意味論を固定してから是正。
- **是正(案A 採択)**: `CountIntegrityReviewEventsAsync` の SQL を駆動側反転
  (pending COUNT〔covering〕+ missing COUNT〔covering〕− 候補有り missing COUNT〔pending 駆動
  JOIN・`COUNT(DISTINCT m.id)`〕)へ置換。旧 `p.sync_folder_id = m.sync_folder_id` は外側
  `m.sync_folder_id=@F` から確定するため両側 `@SyncFolderId` 明示と等価。シグネチャ・呼び出し側は無変更。
- **検査封止**: CP-INTEGRITY-036 へ事象カウント経路の計算量観点 1 行(plan pin の 2 条件+同値ベクタ)。
- **R8(独立セルフレビュー)= 実施・未処置スコープ内所見 0**: fresh-context reviewer の所見=
  [Med] 1(駆動側反転で `m.sync_folder_id`/`m.status='missing'` が単独 load-bearing 化したのに
  同値ベクタが検出域外)+[Low] 2(candidate NULL/不存在 id 未被覆・plan pin の index 選択依存)+
  等価性 5 クラス(hash NULL=スキーマ上不存在・normal/deleted 先・不存在 id・NULL・フォルダ境界)は
  全一致で所見 0。処置= 失格クラス 4 行(別フォルダ missing 先/normal 先/NULL/不存在 id)+
  フォルダ境界の対称 assert をベクタへ追加(10→14 件・条件削除変異は 14→13 で赤)。
  plan pin の index 選択依存は assertion 2(covering ≥2)との二重封止で受理(レビュー判定どおり)。
- **R7= 対象外**: SQL 内部のみ・UI/視覚変更なし。横断規約(ECO-080)= 文言/表示なしで非該当。
- **実 DB 検証(read-only)**: production 新 SQL 原文の抽出実行= **262,045(旧実装と bit 一致)・
  30ms(是正前 318ms・約 10 倍)**。covering index の物理下限(27ms)に一致。
- **diff 規模**: `ImageRepository.cs` +26/-10(SQL+コメントのみ)・`CpIntegrityReviewTests.cs`
  +95 行(2 テスト)・`33-control-plan.yaml` +1 行。§5 影響 BOM 内のみ。
- **機械受入(4 点・全緑)**: `dotnet build` 0 error/0 warning・`ViewPrism2.Tests` **974/974**
  (plan pin は red→green 反転)・`ViewPrism2.Oracle` **109 pass/4 known skip/0 fail**かつ
  diff 0= 凍結オラクル無接触(R6)・`validate_bom` 0 error/0 warning。

## §残ゲート(更新)

- **gate①(裁定)= 不要で確定**(実装層・件数 bit 一致を同値ベクタ 14 件+実 DB 262,045 一致で実測。
  反転版に意味論差なし= R8 等価性レビュー全クラス一致)。
- **gate②(golden)= n/a 提案**: 挙動 bit 一致+視覚変更なし= ECO-134/137 先例どおり機械受入+
  同値プローブがクローズ条件。maintainer の受理で /eco-accept へ。

## §8 クローズ(2026-08-03)

- **gate② 裁定= n/a 受理**(maintainer 2026-08-03): 挙動 bit 一致(同値ベクタ 14 件+実 DB
  262,045 一致)+視覚変更なしのため実機 golden は不要と裁定。機械受入(build 0/0・Tests 974/974・
  Oracle 109+4skip 無接触・validate 0/0)+plan 形状 pin+実 DB 実測(318ms→30ms= covering
  物理下限)がクローズ条件= ECO-134/137 の実機不要裁定と同型。
- **再発防止(CP)**: CP-INTEGRITY-036 characteristic へ「事象カウント経路の計算量= missing
  母集合規模の相関プローブ×行取得を許さない」を**潜伏実績つき**で明記
  (fb5173c から 10 日潜伏・受入 TempDb の小規模母集団がマスキング要因)。検査実体=
  plan 形状 pin(相関 candidate プローブ署名の不在+covering カウント 2 本)+計数同値ベクタ 14 件。
- **M4 同期= 不要**: repository 内部の SQL 形のみの是正で、spec/E-BOM/M-BOM/35-dsbom に
  as-built 乖離なし(ECO-038 と同型)。
- **教訓**: **対話経路(切替・クリック応答)に同期で乗る集計 SQL は、画面に見える件数ではなく
  「走査母集合の規模」で受入する。** 相関サブクエリの駆動側は母集合の小さい側(本件= pending)に
  固定し、意味論等価の反転(全体数 − 補集合数)で物理下限へ落とせるかをまず検討する。受入 fixture の
  母集団規模が小さいと計算量欠陥は全緑のまま潜伏する(本件 10 日)ため、計算量の受入は結果値でなく
  **plan 形状(相関プローブの不在・covering 化)**で pin する= 固定時間閾値なしの原則と両立する形。
  read-across: ECO-134(候補照合 O(M×N)→写像化)・ECO-137(行ごと File.Exists→列挙 1 回)に続く
  「毎行プローブ→集合演算 1 回」クラスの 3 例目で、対象が FS 走査から SQL 実行計画へ拡張された
  (26万件経路 N=8 例目)。BomDD 昇格候補= 「集計/照合の受入は母集合スケールの実測 or 計算量構造の
  pin を必須とする」(playbook 性能封止節への追記形)。
