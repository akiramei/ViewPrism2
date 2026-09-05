ECO-139/140 の BOM 反映点検と訂正を完了しました。

- [30-ebom.yaml](<pilot>/imp-old-2/bom/30-ebom.yaml:323)
  - 旧 Repair consumer・入口依存を `E-UI-INTEGRITY-050` へ移管
  - 旧面の rationale を superseded と明記
  - 統合入口と `E-DESIGN-028` の consumer を補完

- [32-mbom.yaml](<pilot>/imp-old-2/bom/32-mbom.yaml:496)
  - `M-RELINK-025` を一本化後の選別・単発・原子/混在バッチ契約へ更新
  - 削除済み `RepairWindow` / `PendingReviewWindow` unit を superseded 化
  - 削除事実と `M-UI-INTEGRITY-055` への統合先を `path_note` に記録
  - 実装由来の所有ファイル・メソッド・fixture に `(observed@79123df)` を付与
  - FMEA-031/034 の所有先を再配置

- [33-control-plan.yaml](<pilot>/imp-old-2/bom/33-control-plan.yaml:781)
  - 削除済み旧 fixture と現行 fixture の移管関係を明記
  - `CP-INTEGRITY-036` に `CpRelinkUnifiedBatchTests` を追加
  - 旧 pending/repair 検査を歴史記録として区別

編集根拠と未編集判断は [CHANGELOG.md](<pilot>/imp-old-2/CHANGELOG.md:1) に記録しました。`34-routing.yaml` は歴史 route の書き換えになるため未編集・要裁定としています。

静的確認では対象 E/M/CP ID の重複なし、統合後 artifact/fixture の現存、旧 artifact/fixture の不在を確認済みです。制約どおり git・ネットワーク・ビルド・テスト・アプリ実行は使用していません。