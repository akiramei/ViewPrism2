点検と BOM 訂正を完了しました。

- [30-ebom.yaml](<pilot>/pkg-improver/bom/30-ebom.yaml:323)
  - 旧修復面への依存・consumer を統合裁定面へ変更
  - superseded の根拠と存続範囲を明記
- [32-mbom.yaml](<pilot>/pkg-improver/bom/32-mbom.yaml:494)
  - relink の所有を `M-RELINK-025` へ整理
  - 削除済み API 契約を除去
  - 旧 Repair/Pending unit を `superseded` 化
  - 削除 artifact と統合先を記録
  - 実装観測へ `(observed@79123df)` を付記
- [33-control-plan.yaml](<pilot>/pkg-improver/bom/33-control-plan.yaml:781)
  - 削除済み fixture を無効化
  - 後継 `CP-INTEGRITY-036` と `CpRelinkUnifiedBatchTests` への移管を補完

変更一覧、ECO の設計変更、根拠、未編集・要裁定事項は [CHANGELOG.md](<pilot>/pkg-improver/CHANGELOG.md:1) に記録しました。

制約どおり `code/`・`eco/` は変更せず、git・ネットワーク・build・test・製品コード実行も使用していません。