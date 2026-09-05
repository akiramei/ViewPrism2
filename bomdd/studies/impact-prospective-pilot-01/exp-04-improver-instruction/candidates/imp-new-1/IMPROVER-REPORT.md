点検・訂正を完了しました。

主な修正:

- E-BOM の relink owner/consumer、統合裁定面の DB・hash・criteria 依存を補完。
- `E-UI-MODE-041` の旧「修復」依存を統合裁定面へ更新。
- `M-SCAN-005` から relink 所有権を除去し、`M-RELINK-025` へ一本化。
- 現行 repository、SQL 集計、原子バッチ、公開 API を `(observed@79123df)` 付きで記録。
- 削除済み Repair/Pending Window・VM・テストを `superseded/deleted` とし、統合先を明記。
- Control Plan の旧 fixture を現行の Integrity/Relink fixture へ移管。
- 全 diff hunk を棚卸し:
  - ECO-139: 75/75
  - ECO-140: 137/137
- 観測先として記載した現行 artifact は全件存在確認済み。
- 対象 id の重複なし、削除済み artifact を通常パスとして指す記述なし。

編集ファイル:

- [30-ebom.yaml](<pilot>\imp-new-1\bom\30-ebom.yaml:87)
- [32-mbom.yaml](<pilot>\imp-new-1\bom\32-mbom.yaml:28)
- [33-control-plan.yaml](<pilot>\imp-new-1\bom\33-control-plan.yaml:100)
- [34-routing.yaml](<pilot>\imp-new-1\bom\34-routing.yaml:136)
- [CHANGELOG.md](<pilot>\imp-new-1\CHANGELOG.md:1)

制約に従い、ビルド・テスト・git・ネットワークは使用していません。