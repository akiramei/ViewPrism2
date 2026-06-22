# BOM-DD PLM ViewPrism2 AI Repair Queue

BOM-DD PLMの準拠レビューから生成したViewPrism2成果物向けの修復作業票です。
BomDD方法論側またはBOM-DD PLM側のECRはここには含めません。

| Candidate | Title | Status | Ticket | Target |
| --- | --- | --- | --- | --- |
| ECO-PROJ-001 | ViewPrism2成果物: As-Built の 3 件を是正する | Proposed | ECO-PROJ-001.md | bomdd/50-as-built.yaml |
| ECO-PROJ-002 | ViewPrism2成果物: 要求未接続のE-BOMがあります | Proposed | ECO-PROJ-002.md | bomdd/30-ebom.yaml |
| ECO-PROJ-003 | ViewPrism2成果物: ECO / ECR の 3 件を是正する | Applied* | ECO-PROJ-003.md | bomdd/61-impact-analysis.md |
| ECO-PROJ-004 | ViewPrism2成果物: ECO本文は多いが変更registerがありません | Applied* | ECO-PROJ-004.md | bomdd/60-change-register.yaml |
| ECO-PROJ-005 | ViewPrism2成果物: 未解決事項ファイルに解決済み裁定が混在しています | Proposed | ECO-PROJ-005.md | bomdd/ui/image-tab/unresolved-questions.md |
| ECO-PROJ-006 | ViewPrism2成果物: 未解決事項ファイルに解決済み裁定が混在しています | Proposed | ECO-PROJ-006.md | bomdd/ui/unresolved-questions.md |

> `Applied*` = ViewPrism2 成果物側の是正は適用済。`*` は **BOM-DD PLM の bomdd 再同期による Finding 解消の確認待ち**(各票の検証手順)を示す。
> - ECO-PROJ-003: 61/62/63 を「未採用テンプレート」と明示(disposition バナー追加)。影響分析・不要改変監査は各 ECO 本文に直書き運用、移行オラクルはスキーマ変更 ECO 皆無で出番なし、を宣言。
> - ECO-PROJ-004: `bomdd/60-change-register.yaml` を新規作成(ECO-001〜015 を状態・影響BOM・検証・本文パスで構造化)。あわせて `bomdd/00-manifest.yaml`(正本宣言)を追加。
> 残(別パス): ECO-PROJ-001/002(as-built / E-BOM 要求接続)・ECO-PROJ-005/006(unresolved-questions の open/decision 分離)。
