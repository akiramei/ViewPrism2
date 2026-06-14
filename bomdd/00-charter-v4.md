# Charter — ViewPrism2 Loop V4(修復ライフサイクル完結)

<!-- 固定の強さ(phase0-charter.md):
     工場構成・予算・役割・境界種別 = この時点で固定(以後不変)
     題材・スコープ = 仮置き可。`(仮)` を付け、Phase 1 終了時(G1)に確定 -->

- experiment: loop-v4-repair-lifecycle
- 起案日: 2026-06-14(スコープ+裁定 6 点はユーザー maintainer が同日確定 — §裁定記録)
- 基準: Loop V3 完了状態(commit 9467bbd・BOM v3.0・凍結オラクル tag loop-v1-r1 + loop-v2-r1 + loop-v3-r1・
  golden G1〜G9 全 approved・P-07/P-08/P-09 = scaled-decode adapter 採用済み)

## 題材
- 何を作るか(1〜3行):
  Loop V1/V2/V3 で完成した ViewPrism2 に対する増分ループ。**画像ライフサイクルの修復系を完結**させる。
  V3 が開いた `deleted` 状態(マージ元)とトラッシュ閲覧を、**復元・完全削除**で閉じる。あわせて
  missing/pending の **relink(再リンク)候補探索を criteria 条件検索で正式化**する(原典 RVP-REPAIR-004 / RVP-STATUS-003/004)。
- 種別: GUI(デスクトップアプリケーション)— V1/V2/V3 と同じ
- 黒箱境界:
  - 核(**状態遷移規則**・criteria 条件マッチ・relink 確定規則・復元/完全削除規則): in-process(観測契約が必要)
  - 表面(criteria 検索 UI・修復 UI・トラッシュ復元/完全削除 UI・**非破壊明示文言**): GUI(golden+承認者が必要)

## 状態機械(G0 で閉じる — 本ループの中核設計)
状態: **Normal / Pending / Missing / Deleted**(Core/Models/Enums.cs に既存)。遷移を以下に固定する
(△=本ループ V4 で新規/拡張、無印=既存 V1〜V3)。

| # | 遷移 | トリガ | 効果 | 出自 |
|---|---|---|---|---|
| T1 | (新規)→ **Normal** | スキャン: 新規ファイル・missing 一致なし(ScanJudge 3b) | normal 登録 | V1 |
| T2 | (新規)→ **Pending** | スキャン: 新規ファイルが同フォルダ同ハッシュ missing に一致(3a) | pending 登録 + candidate_link_id | V1 |
| T3 | Normal → **Missing** | スキャン: 記録パスのファイルが消失 | missing 化(ID 不変) | V1 |
| T4 | Missing + Pending → **Normal** | relink 確定(CommitRelink) | missing 行へパス上書き・status=normal・candidate_link_id=NULL / **pending 行は消費削除** | V1(V4 で候補探索拡張) |
| T5 | Normal → **Deleted** | マージ元の論理削除 | status=deleted(物理非破壊) | V3 |
| **T6 △** | Deleted → **Normal** | トラッシュ復元 **かつ 記録パスに物理ファイルが存在** | status=normal | **V4** |
| **T7 △** | Deleted → **Missing** | トラッシュ復元 **だが 物理ファイル不在** | status=missing(normal へ戻さない=幽霊 normal 防止) | **V4** |
| **T8 △** | Deleted → **(行消滅)** | 完全削除 | images 行 DELETE → image_tags/features/similarity は FK CASCADE。**物理ファイルは非破壊** | **V4** |

- **復元の存在チェック(裁定 2)**: `Deleted→Normal` は物理存在時のみ。不在は `Deleted→Missing`。これで「存在しない画像が
  normal 表示へ戻る」事故を防ぐ(missing なら relink 経路へ自然に乗る)。
- **完全削除の INV-009 境界(裁定 3)**: DB レコード削除のみ。**物理画像ファイルには一切触れない**(削除・移動・新規作成なし)。
  CP-MERGE-001(マージ L3 物理差分)と同型の **L3 物理差分 CP(復元・完全削除前後でファイル集合/SHA-256/mtime 不変)** で実証。

## スコープ
- 含む — Loop V4(スコープ裁定 2026-06-14 — §裁定記録。要求台帳 REQ-068〜 へ G1 で収束):
  - **criteria 条件検索(核+表面)**: hash / 名前 / 拡張子 / mtime / サイズ の **AND** 結合・**同一コレクション内**・
    対象 status は用途別に固定(G2 で監査)。relink の候補探索メカニズムとして正式化し、単体検索としても提供
  - **relink / 修復(RVP-REPAIR-004)の拡張・明文化**: 既存 RelinkService(exact-hash 候補・missing 側 ID 不変・
    pending 消費削除・candidate_link_id=NULL)を criteria 候補探索で拡張。**candidate_link_id は「候補ヒントであって
    最終真実ではない」と明記**(裁定 5)。replacement に使った pending は常に削除・missing 側を更新・候補複数時は安定順
  - **トラッシュ復元(RVP-STATUS-003)**: T6/T7。物理存在時 normal・不在 missing(裁定 2)
  - **完全削除(RVP-STATUS-004)**: T8。images 削除・FK CASCADE・物理非破壊(裁定 3)。
    UI 文言は物理削除に見えないよう非破壊を明示(「DB から完全に除去」等 — 裁定 6)
- 含まない — 後続ループ / ECO で追加(理由付き):
  - **ORB / detailed 精度モード**(OpenCvSharp/Emgu): 新規ネイティブ依存 + 別 ADR。P-09 の adapter 基盤を将来活用(後続)
  - **作業スペース・バックアップ**(チャーター後続)
  - **タグ制御モード**(viewer-spec §5・V2 で defer 済み)
  - 既存 V1/V2/V3 の defer 群(動的ソート・view_revisions 等)は据え置き

## 工場構成(受入経済性 — playbook §5.2)— V1/V2/V3 踏襲で固定
- ティア: 最小(1工場)— 実用適用。マルチファクトリばらつき測定は行わない
- 使用モデル: Claude(隔離サブエージェント・fresh)。供与物は M-BOM + K-BOM + Routing + Work Order
  +改訂済み仕様/E-BOM のみ。原典 TypeScript ソース・porting-spec・設計対話・固定オラクル(41)・
  探索プローブ(42)は工場非開示
- 収束ループ予算(回数上限): 製造単位あたり 3 回
- 補助: Codex(別系統モデル)を**設計者側**の言語/ライブラリ知識検証・敵対的検証に随時起用可。
  成果物で評価し spec/オラクルで裏取り。**工場非開示の原則は維持**

## 役割
- 設計者(ユーザー+設計AI): maintainer + Claude Code(メインセッション)。リバース工程の原典読解は設計者側のみ
- 仕様監査リーダー数(G2): 1(実用適用につき簡略。仕様完了ゲート自体は実施)
- 承認者(知覚・golden): maintainer(criteria 検索 UI・修復フロー・トラッシュ復元/完全削除の golden 受入 = G-10)

## 前提・制約
- V3 基準ビルドからの増分。BOM は v3.0 → v4.0(台帳 10/20/30/31/32/33/34 は同一ファイルを改訂継続)
- V1/V2/V3 凍結オラクル(loop-v1-r1 = S-01〜12、loop-v2-r1 = S-13〜18、loop-v3-r1 = S-19〜25 +
  this-build golden 等)は回帰検査として不変のまま全ループ維持。V4 新規固定オラクルは製造前に凍結(tag loop-v4-r1 予定)
- **INV-009(物理画像ファイル非破壊)の第 2 の実アクション**: V3 マージ(status→deleted)に続き、V4 の
  **復元・完全削除**が status 書換/レコード削除を行う。**L3 物理差分 CP(S-26 TrashPhysicalDiff 系)を追加**し、
  復元・完全削除前後で物理ファイル集合/SHA-256/mtime が不変であることを実証する
- `images.candidate_link_id` 列は既存(V1)。relink 核(RelinkService.CommitRelinkAsync)も既存 → V4 は
  **大改造でなく criteria 候補探索の追加と意味論の明文化**が主(裁定 5)
- ADR: 状態機械の追加遷移(復元/完全削除)に新規ネイティブ依存はなし。criteria 検索も既存基盤(SQLite/ファイルメタ)で完結
  → 原則 ADR 不要(G1 で再確認)
- S-BOM(53-service-bom): 新規依存ピンなし(G1 で再確認)

## 裁定記録(2026-06-14 — 起案時にユーザー maintainer が確定)
| # | 項目 | 裁定 | 適用先 |
|---|---|---|---|
| 1 | スコープ | **3 件セット = 修復ライフサイクル完結**(criteria 検索 + relink 拡張 + トラッシュ復元/完全削除) | 本チャーターのスコープ |
| 2 | 復元の存在チェック | `Deleted→Normal` は**物理存在時のみ**。不在は `Deleted→Missing`(復元拒否でなく missing 復元) | 状態機械 T6/T7 |
| 3 | 完全削除の INV-009 | `images` 削除・`image_tags`/features/similarity は FK CASCADE・**物理ファイル非破壊** | 状態機械 T8 / L3 物理差分 CP |
| 4 | criteria 検索 | hash/name/ext/mtime/size は **AND**・**同一 collection 内**・対象 status は用途別に固定(G2 で監査) | criteria 検索の仕様 |
| 5 | relink 意味論 | missing を保持・replacement/pending は消費後に削除・候補複数時は安定順。**candidate_link_id は候補ヒントであって最終真実でない** | relink の仕様 |
| 6 | UI 文言 | 「完全削除」は物理削除に見えるため**非破壊を明示**(「DB から完全に除去」等) | トラッシュ UI |

## バックログ出典(起案入力のトレース)
- RVP-REPAIR-004(relink・candidate_link_id・consumedCandidate の改善移植): charter-v2 §裁定記録 2 / charter-v3 §含まない / porting-spec 07・09
- RVP-STATUS-003/004(トラッシュ復元・完全削除): charter-v3 §含まない / 20-spec §2.10.3 注記(完全削除/復元は後続ループ)
- criteria 条件検索(hash/name/ext/mtime/size AND): charter-v3 §含まない(「実質リンク修復用途 → V4」)/ CPOL-008(criteria/similar 保存)
- 既存実装: RelinkService.CommitRelinkAsync / ScanJudge(3a pending+candidate_link_id)/ ImageStatus enum / images.candidate_link_id 列
- INV-009 woven 安全制約: 20-spec.md(V2 格上げ)・CP-MERGE-001(マージ L3 物理差分)を復元/完全削除へ横展開
- 互換ポリシー: CPOL-008(criteria/similar 保存)・CPOL-009(論理操作)・CPOL-103(pHash adapter)

## 完了の定義
- V4 固定オラクル(**状態遷移表 T1〜T8**・criteria 条件マッチ・relink 確定規則・復元の存在分岐・完全削除の CASCADE・
  **S-26 復元/完全削除 L3 物理差分=物理非破壊**、製造前凍結)全通過
  + V1/V2/V3 凍結オラクル回帰ゼロ
- blocker ずるゼロ
- 納品物: 成果物 + bomdd/ 改訂一式(BOM v4.0・オラクル・治具・As-Built・cheat-log・metrics)
- L3 物理差分 CP: 復元・完全削除前後で物理画像ファイルが無変化であることを実証(INV-009 第 2 の実アクション)
- golden 承認 G-10: criteria 検索 UI + 修復フロー + トラッシュ復元/完全削除(非破壊文言含む)(承認者 maintainer)
