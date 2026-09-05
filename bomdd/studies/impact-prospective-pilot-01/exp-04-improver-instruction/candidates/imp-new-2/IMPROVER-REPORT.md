点検・訂正を完了しました。

- [30-ebom.yaml](/<pilot>/imp-new-2/bom/30-ebom.yaml:87)
  - 統合裁定面の依存・consumer を補完
  - Pending 面を superseded 化
  - Repair 面の部分統合範囲を明記
  - relink の所有を `E-RELINK-007` に一本化

- [32-mbom.yaml](/<home>/AppData/Local/Temp/claude/C--Users-<user>/source/repos/BomDD/5c277f20-f7c4-4013-9a5a-3fc63f700a77/scratchpad/pilot/imp-new-2/bom/32-mbom.yaml:93)
  - Scan・DB・Relink・統合 UI の unit、artifact、公開契約を訂正
  - 削除済み Window/ViewModel を superseded artifact として保持
  - 現行実装の所在・メソッドを `(observed@79123df)` 付きで追記
  - 旧 Repair/Pending 契約を統合先と存続部分に分離

- [33-control-plan.yaml](/<home>/AppData/Local/Temp/claude/C--Users-<user>/source/repos/BomDD/5c277f20-f7c4-4013-9a5a-3fc63f700a77/scratchpad/pilot/imp-new-2/bom/33-control-plan.yaml:781)
  - 削除済み旧 fixture と後継 Integrity/Relink fixture の写像を訂正

- [CHANGELOG.md](/<home>/AppData/Local/Temp/claude/C--Users-<user>/source/repos/BomDD/5c277f20-f7c4-4013-9a5a-3fc63f700a77/scratchpad/pilot/imp-new-2/CHANGELOG.md:1)
  - 編集根拠、設計変更、公開契約写像、未編集事項を記録
  - ECO-139 の75 hunk、ECO-140 の137 hunk、計212 hunkを分類
  - exact header 照合で未収録 hunk は0件

`34-routing` は歴史的製造ルートであり、ECOに更新根拠がないため未編集です。コード側の所有コメントの陳腐化と未使用repository APIは「実装の逸脱の疑い／要裁定」として記録しました。

制約に従い、ビルド・テスト・git・ネットワークは使用していません。