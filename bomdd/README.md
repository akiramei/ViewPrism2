# BomDD テンプレ一式(フォワード・モード)

[bomdd-playbook-v1.md](../bomdd-playbook-v1.md) の各フェーズ成果物のテンプレ。
YAML の語彙は v2 実証リポジトリ([BomDD-WebApi-Sample](https://github.com/maintainermei/BomDD-WebApi-Sample) `bomdd/`、[BomDD-DistributedSaga-Sample](https://github.com/maintainermei/BomDD-DistributedSaga-Sample))で実運用した形式をミラーしている。**JSON Schema ではない**(硬化は [schema-candidates-index.md](../schema-candidates-index.md) §5 の昇格条件を満たすまでしない)。

対象プロジェクトのリポジトリ直下に `bomdd/` を作り、コピーして埋める。

| 番号 | ファイル | フェーズ | 渡し先 |
|---|---|---|---|
| 00 | [00-charter.md](00-charter.md) | Phase 0 チャーター | 設計者 |
| 10 | [10-requirements.yaml](10-requirements.yaml) | Phase 1 要求台帳 | 設計者 |
| 20 | [20-spec.md](20-spec.md) | Phase 2 仕様書 | 設計者→**製造パッケージに含む** |
| UI | [ui-mock-extraction/](ui-mock-extraction/) | Phase 2–3 UIモック抽出(candidate) | 設計者(E-BOM 前段。工場へ渡す場合は20/30–34へ昇格後) |
| 30 | [30-ebom.yaml](30-ebom.yaml) | Phase 3 E-BOM | 製造パッケージ |
| 31 | [31-kbom.yaml](31-kbom.yaml) | Phase 3 K-BOM | 製造パッケージ |
| 32 | [32-mbom.yaml](32-mbom.yaml) | Phase 3 M-BOM | 製造パッケージ |
| 33 | [33-control-plan.yaml](33-control-plan.yaml) | Phase 3 Control Plan | 製造パッケージ |
| 34 | [34-routing.yaml](34-routing.yaml) | Phase 3 Routing | 製造パッケージ |
| 35 | [35-design-system-bom.yaml](35-design-system-bom.yaml) | Phase 3 Design System BOM(shared-component sub-BOM candidate) | 製造パッケージ(coverage gate として必要に応じて33へ同期) |
| 40 | [40-work-order.md](40-work-order.md) | Phase 4 製造指示 | 製造パッケージ(表紙) |
| 41 | [41-fixed-oracle.yaml](41-fixed-oracle.yaml) | Phase 3–5 固定オラクル | **設計者のみ(工場非開示)** |
| 42 | [42-exploratory-probes.yaml](42-exploratory-probes.yaml) | Phase 3–5 探索プローブ | **設計者のみ(工場非開示)** |
| 43 | [43-visual-gap-analysis.md](43-visual-gap-analysis.md) | Phase 5 視覚ギャップ分析(UI-CAD vs 実機) | 設計者(検査結果。是正時はCAPAへ) |
| 50 | [50-as-built.yaml](50-as-built.yaml) | Phase 5–6 製造来歴 | 記録 |
| 51 | [51-cheat-log.md](51-cheat-log.md) | 全期間 ずる台帳 | 記録 |
| 52 | [52-metrics.yaml](52-metrics.yaml) | Phase 5 測定 | 記録 |
| 53 | [53-service-bom.yaml](53-service-bom.yaml) | Phase 6 保守部品表(概念は [s-bom-template.md](../s-bom-template.md)) | 納品物 |
| 60 | [60-change-order.md](60-change-order.md) | Phase 7 変更/是正オーダー(ECO/CAPA: 影響分析→部分再製造→回帰) | 設計者(改訂 BOM+ECO/CAPA を工場へ) |
| 61 | [61-impact-analysis.md](61-impact-analysis.md) | Phase 7 影響分析(影響なし予測の先行凍結) | 製造パッケージ(ECO/CAPA 時) |
| 62 | [62-migration-oracle.md](62-migration-oracle.md) | Phase 7 データ移行オラクル+fixture | **設計者のみ(実装・fixture 期待値は工場非開示)** |
| 63 | [63-diff-audit.md](63-diff-audit.md) | Phase 7 不要改変監査(diff 基準点+4分類) | 設計者(「diff を測る」の事前宣言のみ work order へ) |

**隔離規律(再掲)**: 製造装置に渡してよいのは 20/30–34/40(+観測契約)だけ。41/42 と設計対話の履歴は渡さない。
