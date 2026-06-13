# Charter — ViewPrism2 Loop V3(類似検索+マージ)

<!-- 固定の強さ(phase0-charter.md):
     工場構成・予算・役割・境界種別 = この時点で固定(以後不変)
     題材・スコープ = 仮置き可。`(仮)` を付け、Phase 1 終了時(G1)に確定 -->

- experiment: loop-v3-similarity
- 起案日: 2026-06-13(スコープ裁定 2 点はユーザー maintainer が同日確定 — §裁定記録)
- 基準: Loop V2 完了状態(commit 99f8d8e・BOM v2.0・凍結オラクル tag loop-v1-r1 + loop-v2-r1・golden G1〜G8 全 approved)

## 題材
- 何を作るか(1〜3行):
  Loop V1/V2 で完成した ViewPrism2 に対する増分ループ。原典 view-prism の
  **類似画像検索(pHash ベース)と画像マージ**(RVP-SIM-*/RVP-MERGE-*)を移植する。
  知覚ハッシュ計算核(DCT)+ハミング距離+類似度グルーピング+結果表示 UI、
  および重複画像のマージ(タグ集約 union + マージ元の論理削除・物理非破壊)。
- 種別: GUI(デスクトップアプリケーション)— V1/V2 と同じ
- 黒箱境界:
  - 核(pHash 計算・ハミング距離・類似度変換・グルーピング規則・マージ規則): in-process(観測契約が必要)
  - 表面(類似検索 UI・結果表示・マージ操作・マージ元 deleted の最小可視化): GUI(golden+承認者が必要)

## スコープ
- 含む — Loop V3(スコープ裁定 2026-06-13 — §裁定記録 1/2。要求台帳 REQ-061〜 へ G1 で収束):
  - **pHash 計算核**: 32×32 リサイズ→グレースケール→2D DCT→低周波 8×8=64bit→中央値しきい→16hex
    (原典 phash-utils.ts 準拠。SkiaSharp(既存)でピクセル取得 — 新規ネイティブ依存なし)
  - **類似検索(similar / pHash・simple モードのみ)**: 対象 status=normal/pending/missing(deleted・自分自身除外)・
    同一コレクション内全数比較・ハミング距離(popcount XOR, 0–64)→段階式類似度%・しきい値(既定 70, 50–100)・
    類似度降順ソート
  - **特徴量/類似度キャッシュ**: image_features(phash)・image_similarity(cache_key=正規化 id ペア)。
    再計算条件(file_size/modified_date/hash 変化)で invalidation
  - **結果表示 UI**: 類似画像群を類似度付きで一覧・選択
  - **マージ(RVP-MERGE-001/002)**: タグ集約 union + マージ元 status→deleted の論理操作。
    **物理画像ファイルは非破壊**(INV-009 woven 安全制約の初の実アクション適用 → L3 物理差分 CP で実証)。
    値付きタグ(textual/numeric)衝突 = マージ先優先(マージ先に値あれば保持・なければマージ元採用 — §裁定記録 3)
  - **マージ元 deleted の最小可視化**: マージで deleted 化した画像が「消えたまま見えない」を避ける最小トラッシュ表示
    (一覧のみ。復元/完全削除の本格 UI は後続ループ)
- 含まない — 後続ループ / ECO で追加(理由付き):
  - **ORB / detailed モード**(pHash+ORB 特徴量融合): pHash-only に確定(§裁定記録 2)。
    OpenCV ネイティブ依存(OpenCvSharp/Emgu.CV)を回避し計算核を純粋に保つ。CPOL-103(実装は adapter 許容)・
    RVP-REPAIR-005(pHash-only 是正)と整合。原典でも detailed は遅く UX 課題ありと記録(2025-05-28 issue)
  - **criteria 条件検索(hash/名前/拡張子/mtime/サイズ AND)**: 実質リンク修復用途 → 修復ループ(V4)へ。
    pHash 距離 0 が完全一致相当をカバーするため V3 の重複検出には不要
  - **リンク修復 / relink(RVP-REPAIR-004)**: missing/pending の再リンク・candidate_link_id・consumedCandidate。
    状態遷移拡張が大きく独立ループ(V4)が妥当。裁定済み(charter-v2 §裁定記録 2)・実装は V4
  - **トラッシュの復元・完全削除(RVP-STATUS-003/004)**: 永久削除は image_tags/images レコード削除(物理非破壊)。
    V3 はマージ元 deleted の可視化までとし、復元/完全削除フローは後続ループ
  - **作業スペース・バックアップ**(チャーター後続)
  - 既存 V1/V2 の defer 群(動的ソート・view_revisions 等)は据え置き

## 工場構成(受入経済性 — playbook §5.2)— V1/V2 踏襲で固定
- ティア: 最小(1工場)— 実用適用。マルチファクトリばらつき測定は行わない
- 使用モデル: Claude(隔離サブエージェント・fresh)。供与物は M-BOM + K-BOM + Routing + Work Order
  +改訂済み仕様/E-BOM のみ。原典 TypeScript ソース・porting-spec・設計対話・固定オラクル(41)・
  探索プローブ(42)は工場非開示
- 収束ループ予算(回数上限): 製造単位あたり 3 回
- 補助: Codex(別系統モデル)を**設計者側**の言語/ライブラリ知識検証に随時起用可
  (pHash の DCT 実装・SkiaSharp ピクセルアクセス・原典 sharp との等価性確認・敵対的検証)。
  成果物で評価し spec/オラクルで裏取り。**工場非開示の原則は維持**(Codex を工場として使う場合は原典非開示)

## 役割
- 設計者(ユーザー+設計AI): maintainer + Claude Code(メインセッション)。
  リバース工程での原典ソースコード読解は設計者側のみ可(Codex を設計者側補助で使う場合も同様の線引き)
- 仕様監査リーダー数(G2): 1(実用適用につき簡略。仕様完了ゲート自体は実施)
- 承認者(知覚・golden がある場合は必須): maintainer(類似検索 UI・結果表示・マージ操作の golden 受入)

## 前提・制約
- V2 基準ビルドからの増分。BOM は v2.0 → v3.0 へ改訂(台帳 10/20/30/31/32/33/34 は同一ファイルを改訂継続)
- V1/V2 凍結オラクル(tag loop-v1-r1 = S-01〜12、loop-v2-r1 = S-13〜18、計 18 ケース)は
  回帰検査として不変のまま全ループ維持。V3 の新規固定オラクルは製造開始前に凍結する(tag loop-v3-r1 予定)
- **INV-009(物理画像ファイル非破壊)woven 安全制約の初の実アクション適用**:
  V2 で 20-spec へ woven 格上げ済み。V3 のマージが初めて status を deleted へ書き換える「実アクション」。
  charter-v2 §前提が予告した通り、本ループで **L3 物理差分 CP(マージ前後で物理ファイル無変化を実証)を追加**する
- 新規 DB テーブル(image_features・image_similarity)+マイグレーション → 自作マイグレーションランナーで追加。
  原典マイグレーション 006(candidate_link_id)/007/008 を参照(候補リンクは V3 では未使用 — 修復ループへ)
- ADR 必須(docs/adr/): pHash 実装方式(DCT 自作実装の根拠・原典等価性・SkiaSharp 採用)。
  ORB defer により新規ネイティブ依存はなし(将来 ORB 採用時に別 ADR)
- S-BOM(53-service-bom): pHash は既存依存(SkiaSharp)で完結 → 監視対象の新規ピンは原則なし(G1 で再確認)

## 裁定記録(2026-06-13 — 起案時にユーザー maintainer が確定)
| # | 項目 | 裁定 | 適用先 |
|---|---|---|---|
| 1 | スコープ深度 | **検出+マージまで** — pHash 類似検出 + 結果表示 + マージ(論理・物理非破壊)。修復(relink)・完全削除・ORB は後続ループ | 本チャーターのスコープ |
| 2 | ORB / detailed モード(RVP-SIM-003) | **pHash-only**(simple のみ移植・detailed/ORB は defer)。OpenCV ネイティブ依存を回避 | スコープ除外 + RVP-REPAIR-005 と整合 |
| 3 | RVP-MERGE-002(値付きタグの衝突) | **マージ先優先**(マージ先に値があれば保持・なければマージ元の値を採用)— charter-v2 §裁定記録 4 を本ループで実装 | マージ機能の仕様 |
| 4 | RVP-REPAIR-005(detailed=pHash のみ) | UI を pHash-only に是正(detailed 本実装なし)— charter-v2 §裁定記録 3 を本ループの類似検索にも適用 | 類似検索 UI のモード扱い |

## バックログ出典(起案入力のトレース)
- RVP-SIM-001/003(類似検索 2 方式・simple/detailed・閾値・キャッシュ): view-prism porting-spec 06 §ワークフロー6・02 §7
- RVP-MERGE-001(タグ union + source deleted・物理非破壊)/RVP-MERGE-002(値タグ衝突): porting-spec 06・07・09(CPOL-009/202)
- RVP-REPAIR-005(detailed=pHash のみの乖離): porting-spec 07・09(policy_decision_required)→ §裁定記録 4 で確定
- 互換ポリシー: CPOL-008(criteria/similar 保存)・CPOL-009(マージ論理操作)・CPOL-103(pHash/ORB は preserve_with_adapter)
- E-BOM 候補: EB-VP-SIMILARITY(surface)・EB-VP-MERGE(core)(porting-spec 11)
- CP 候補: CP-SIM-001(pHash 類似度と閾値)・CP-MERGE-001(タグ集約+source deleted+物理非破壊 L3)(porting-spec 11)
- 原典実装リバース(2026-06-13 設計者側調査): phash-utils.ts(DCT 32×32→8×8 64bit)・similarity-evaluator.ts
  (ハミング距離・段階式類似度)・similarity-cache.service.ts・migration 007/008(image_features/image_similarity)・
  RepairModal/SimilarImageSearchModal/ImageMergeModal(UI フロー)
- INV-009 woven 安全制約: bomdd/20-spec.md(V2 Phase 2 で格上げ)・charter-v2 §前提(merge 導入ループで L3 物理差分 CP)

## 完了の定義
- V3 固定オラクル(pHash 計算/ハミング距離/類似度変換/グルーピング/マージ規則/物理非破壊、製造前凍結)全通過
  + V1/V2 凍結オラクル(loop-v1-r1 + loop-v2-r1、18 ケース)回帰ゼロ
- blocker ずるゼロ
- 納品物: 成果物 + bomdd/ 改訂一式(BOM v3.0・オラクル・治具・As-Built・cheat-log・metrics)・pHash ADR
- L3 物理差分 CP: マージ前後で物理画像ファイルが無変化であることを実証(INV-009 実アクション初適用)
- golden 承認: 類似検索 UI(モード/閾値/結果表示)+マージ操作 + マージ元 deleted の可視化(承認者 maintainer)
