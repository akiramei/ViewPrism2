# PILOT-A rubric = 封印した初回裁定(裁定者: Codex gpt-5.6-sol high・独立セッション・予測を見ていない)

- sealed sha256(ADJUDICATION.sealed.md) = 5866bda18546454264eaa41dba74e636fd8fc059677b49f0c4ea9657831b41b1 @ 2026-09-05T02:40:48Z
- 主評価= M unit の変更予測(role=change)と調査候補(change∪investigate)。pass/fail は成功条件ではない(探索的)。

```yaml
initial_adjudication:
  request: REQUEST-R1
  baseline: "79123df"
  units:
    - unit: M-UI-IMAGETAB-035
      role: change
      confidence: certain
      evidence:
        - src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:427
        - src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:436
        - src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:459
      rationale: "コレクション切替は、表示対象の normal 取得に加えて統合裁定件数の取得完了まで待ち、その後に表示完了となる。hidden の missing 件数に表示待ち時間を依存させない変更が必要。"
    - unit: M-DB-007
      role: change
      confidence: likely
      evidence:
        - src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:307
        - src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:322
        - src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:326
        - src/ViewPrism2.Infrastructure/Database/DatabaseManager.cs:9
      rationale: "統合裁定件数 SQL は対象コレクションの missing 全行に対して相関 NOT EXISTS を評価する。単一共有接続も直列化されるため、262,045 件に比例する処理の最適化が必要になる可能性が高い。"
    - unit: M-HARNESS-015
      role: change
      confidence: certain
      evidence:
        - tests/ViewPrism2.Tests/CpUiG1CollectionScopeTests.cs:206
        - tests/ViewPrism2.Tests/CpUiG1CollectionScopeTests.cs:382
        - tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1130
      rationale: "既存の切替テストは missing 1 件で、表示完了が大量 missing の件数計算に阻害されない契約を検査していない。報告データ形状を表す回帰ガードが必要。"
    - unit: M-CORE-001
      role: investigate
      confidence: certain
      evidence:
        - src/ViewPrism2.Core/Repositories/IImageRepository.cs:53
        - src/ViewPrism2.Core/Repositories/IImageRepository.cs:58
        - src/ViewPrism2.Core/Repositories/IImageRepository.cs:73
      rationale: "統合裁定件数は pending と未対応 missing を数える契約であり、単純な missing 除外や概算へ変更できない。DB/UI 改修時の意味論確認が必要だが、公開契約自体の変更は不要と読める。"
  not_affected:
    - unit: M-SIMSEARCH-021
      rationale: "「類似」はコレクション名にすぎず、SelectCollection のロード経路は SimilaritySearchService を呼ばない。"
    - unit: M-PHASH-020
      rationale: "コレクション切替経路には pHash 計算または特徴量検索の呼び出しがない。"
    - unit: M-UI-INTEGRITY-055
      rationale: "切替時に統合裁定ウィンドウは開かれず、遅延を作る件数取得と表示完了制御は ImageTabViewModel にある。"
  contracts_or_tests_to_check:
    - "CP-UI-G1: tests/ViewPrism2.Tests/CpUiG1CollectionScopeTests.cs:382"
    - "統合裁定件数パリティ: tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:838"
    - "大量 missing 応答性: tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1130"
    - "索引追加を行う場合のスキーマ同値: tests/ViewPrism2.Tests/CpDb006Tests.cs:167"
  unresolved:
    - question: "実データで遅延時間の支配項が CountIntegrityReviewEventsAsync だけか、他の直列クエリも寄与するか。"
      how_to_resolve: "再現 DB で LoadContentAsync 内の各 repository 呼び出しを個別計時し、統合裁定件数 SQL の EXPLAIN QUERY PLAN を取得する。"
    - question: "是正を件数取得の表示クリティカルパスからの分離だけで満たせるか、SQL 書換えまたは追加索引も必要か。"
      how_to_resolve: "normal 6・missing 262,045 の再現データで、分離案と SQL/索引案を比較し、表示完了時間と件数バッジの意味論を検証する。"
```
