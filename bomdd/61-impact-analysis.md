# Impact Analysis — <ECO/CAPA-ID> 影響分析(Phase 7)

> **[disposition: 未採用テンプレート / unused-template]**
> 本ファイルは BomDD テンプレ一式の Phase 7 影響分析テンプレ。**本プロジェクトでは独立 working artifact として使っていない。**
> 影響分析は各 ECO 本文(`60-change-order*.md` の §2)に直書き運用している(例: [ECO-002 §2](60-change-order-eco-002.md))。
> ECO の状態・影響BOM・検証の構造化索引は [60-change-register.yaml](60-change-register.yaml)、正本宣言は [00-manifest.yaml](00-manifest.yaml) を参照。
> 下記テンプレ本体は参照用に保持する(新規 ECO で標準テンプレが必要になったときの雛形)。

> [60-change-order.md](60-change-order.md) §2 の詳細版(小規模 ECO なら 60 内に直書きでよい)。
> 規律: 影響「あり」だけでなく**影響なし予測を反証可能な形で製造前に凍結**する。回帰の結果が予測の採点になる(Phase 7 の測定値。forward-01.5 で全的中=under/over-inclusion 0)。
> 粒度: 標準は M-BOM unit。小規模 ECO で影響集合が全 unit に達する場合は **E-BOM 部品粒度+不要改変 diff 測定([63-diff-audit.md](63-diff-audit.md))で代替**(playbook §4.1 実測フィードバック)。
> 欠陥是正では、影響分析は「どこを直すか」だけでなく「欠陥がどの上流成果物に宿ったか」を確定する。原因が仕様/BOM/検査器なら、その上流成果物の改訂を影響ありに含める。ソースだけを影響ありにして始めない。

## 1. 影響あり(トレース逆引き)
| 段 | 影響 ID | 何が変わるか |
|---|---|---|
| 仕様節 | | |
| E-BOM 部品(graph_edges の consumers 含む) | | woven を忘れない |
| M-BOM unit | | 影響集合が全 unit に達したら粒度の観察として記録 |
| Control Plan 特性 | | |
| 固定オラクル | **追加行のみ**(既存行は不変) | |
| 専用オラクル(データ移行等) | | [62-migration-oracle.md](62-migration-oracle.md) |
| K-BOM / 調達部品 | | |
| Routing / Work Order | | 製造指示の改訂が必要なら含める |

## 2. 影響なし予測(反証可能 — 製造前に凍結)
> 製造後の回帰で当たるかを検証する。外れ=**under-inclusion**(取りこぼし。最危険)。
> 既存オラクル行を1行ずつなめ、「なぜこの行は変わらないか」の根拠を書く(根拠が書けない行=影響分析の穴)。

| 領域(既存オラクル行・非対象ファイル群) | 予測の根拠 |
|---|---|
| | |

## 3. 採点(製造・回帰の後に記入)
- under-inclusion(影響なし予測が外れた箇所):
- over-inclusion(影響ありとしたが変わらなかった箇所):
- 粒度の観察(絞り込み効果が出たか):
