# ECO-PROJ-003 ViewPrism2成果物: ECO / ECR の 3 件を是正する

## 宛先
- 対象リポジトリ: ViewPrism2: C:\Demo\source\repos\ViewPrism2
- 変更責務: ProjectArtifacts
- 変更種別: BomSync
- 対象パス: bomdd/61-impact-analysis.md

## 目的
Phase 7成果物に <ECO/CAPA-ID> などのプレースホルダ、または空の記録欄が残っています。 ほか 2 件の関連指摘があります。

## 許可スコープ
ViewPrism2側の bomdd/ 配下成果物だけを主対象にする。既存のアプリケーションソースコードは、成果物修正に必須でない限り変更しない。

## 具体作業
ECO別の実績として記入するか、未使用テンプレートなら archive/template へ移動します。

## 関連Finding
- [Watch] ECO / ECR: 61-impact-analysis.md がテンプレート未記入に見えます (bomdd/61-impact-analysis.md)
- [Watch] ECO / ECR: 62-migration-oracle.md がテンプレート未記入に見えます (bomdd/62-migration-oracle.md)
- [Watch] ECO / ECR: 63-diff-audit.md がテンプレート未記入に見えます (bomdd/63-diff-audit.md)

## 完了条件
bomdd同期後、関連する準拠Findingが解消し、PLM上で対象成果物またはBOM/工程/変更情報が構造化表示されること。

## 検証
ViewPrism2で成果物を修正後、BOM-DD PLMでbomdd同期を実行し、該当候補または関連Findingが減ることを確認する。

## 制約
- 既存成果物を削除せず、必要なら履歴・正本・registerで扱いを分ける。
- 推定で補完せず、根拠がない情報は未設定または要確認として明示する。
- 作業後はBOM-DD PLMの準拠レビューで同じ候補が残るか確認する。