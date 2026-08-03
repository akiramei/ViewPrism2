# ECO-142 — コレクション切替が missing 母集合規模で遅延(事象カウントの駆動側選択)

- 種別: 不具合(実装層・性能特性・**実測済み**。件数の挙動は仕様どおり)
- status: staged(2026-08-03 起票。出典= maintainer 報告「6 件の類似コレクションが 3 桁コレクションより表示が遅い」)
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
