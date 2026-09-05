# ECO-142 予備試験 — 裁定履歴(追記のみ・初回裁定は書き換えない)

対象: 変更要求 R1(maintainer 原報告ベース)/ baseline `79123df` / 実際の是正 commit `55c491d`(ECO-142)。

## 1. 初回裁定(封印・予測を見る前に固定)

- 裁定者: Codex `gpt-5.6-sol`・reasoning effort high・独立新規セッション(read-only sandbox・履歴なしパッケージ `pkg-adjudicator`= code/ + mbom-artifacts.md + REQUEST-R1.md)。
- 封印: `ADJUDICATION.sealed.md` sha256 `5866bda18546454264eaa41dba74e636fd8fc059677b49f0c4ea9657831b41b1` @ 2026-09-05T02:40:48Z。
  同一内容を trials/PILOT-A・PILOT-B の `rubric/rubric.md` に転記(trial.yaml の rubric_hash で封印)。
- 裁定者の実行コマンド 9 件はすべてパッケージ内(events.jsonl で監査・パッケージ外パス/git の使用 0)。

| unit | role | confidence | 要旨 |
|---|---|---|---|
| M-UI-IMAGETAB-035 | change | certain | 切替経路が統合裁定件数の取得完了まで待つ。missing 件数に表示待ちを依存させない変更 |
| M-DB-007 | change | likely | 統合裁定件数 SQL が missing 全行に相関 NOT EXISTS。262,045 件比例の最適化が必要になる可能性が高い |
| M-HARNESS-015 | change | certain | 大量 missing での切替応答性を検査する回帰ガードが無い |
| M-CORE-001 | investigate | certain | 件数契約(IImageRepository)の意味論確認。公開契約の変更は不要と読める |

not_affected: M-SIMSEARCH-021 / M-PHASH-020 / M-UI-INTEGRITY-055。
unresolved: ①支配項が CountIntegrityReviewEventsAsync だけか ②分離だけで足りるか、SQL 書換え/索引も要るか。

## 2. 実 diff との突合(55c491d・裁定の後に開示)

| 実際に変更されたもの | unit | 初回裁定との関係 |
|---|---|---|
| ImageRepository.cs(CountIntegrityReviewEventsAsync の SQL を pending 駆動へ反転・26 行) | M-DB-007 | 裁定 change(likely)と一致 |
| CpIntegrityReviewTests.cs(同値ベクタ+計算量プローブ・90 行) | M-HARNESS-015 | 裁定 change(certain)と一致 |
| 33-control-plan.yaml(CP-INTEGRITY-036 に計算量観点 1 行) | (BOM) | 裁定 contracts_or_tests_to_check には CP-INTEGRITY-036 の fixture 行が挙がっていた |
| ImageTabViewModel.cs | M-UI-IMAGETAB-035 | **変更なし** — 裁定は change(certain)としていた |
| Core(IImageRepository) | M-CORE-001 | 変更なし — 裁定は investigate(契約不変)と一致 |

## 3. 訂正(履歴として追記・初回裁定の本文は不変)

- **訂正 1**: M-UI-IMAGETAB-035 を change → **not required by the chosen fix**。分類= **是正方針の選択**(裁定の unresolved ②「分離だけで足りるか、SQL 書換えも要るか」が実装で「SQL 書換えのみ」に確定した)。裁定の誤りではなく、裁定が 2 案を change 側に倒して固定した結果。実装の逸脱・設計変更・抽出限界のいずれにも当たらないため、分類語彙に **「是正方針の選択で不要化」** を追加する(本予備試験の発見)。
- **訂正 2**: 追加なし — 実 diff の src 変更は裁定の change 集合に包含された(under 0)。
- **未確定の扱い**: unresolved ① は実装側の実測(order §1 の計測表)で「支配項= 当該 SQL」と確定したが、裁定者には開示していない情報のため、裁定の確定手段(個別計時+EXPLAIN)が妥当だったことだけを記録する。

## 4. 評価に用いる集合

- 主評価(封印 rubric): 初回裁定のまま(change= {M-UI-IMAGETAB-035, M-DB-007, M-HARNESS-015} / scope= +M-CORE-001)。
- 参考(訂正後・事後導出・receipt ではない): change= {M-DB-007, M-HARNESS-015} / scope= +M-UI-IMAGETAB-035, M-CORE-001。
  予測が M-UI-IMAGETAB-035 を change に入れても、訂正後の集合では over とは扱わない(裁定者と同じ判断)。
