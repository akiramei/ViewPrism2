# ECO-PROJ-004 ViewPrism2成果物: ECO本文は多いが変更registerがありません

## 宛先
- 対象リポジトリ: ViewPrism2: C:\Demo\source\repos\ViewPrism2
- 変更責務: ProjectArtifacts
- 変更種別: BomSync
- 対象パス: bomdd/60-change-register.yaml

## 目的
15 件のChange OrderがMarkdown本文として存在します。PLMは状態、影響BOM、検証結果を本文から推定します。

## 許可スコープ
ViewPrism2側の bomdd/ 配下成果物だけを主対象にする。既存のアプリケーションソースコードは、成果物修正に必須でない限り変更しない。

## 具体作業
bomdd/60-change-register.yaml を追加し、ECO ID、状態、影響BOM、検証、本文パスを構造化します。

## 関連Finding
- [Watch] ECO / ECR: ECO本文は多いが変更registerがありません (bomdd/60-change-register.yaml)

## 完了条件
bomdd同期後、関連する準拠Findingが解消し、PLM上で対象成果物またはBOM/工程/変更情報が構造化表示されること。

## 検証
ViewPrism2で成果物を修正後、BOM-DD PLMでbomdd同期を実行し、該当候補または関連Findingが減ることを確認する。

## 制約
- 既存成果物を削除せず、必要なら履歴・正本・registerで扱いを分ける。
- 推定で補完せず、根拠がない情報は未設定または要確認として明示する。
- 作業後はBOM-DD PLMの準拠レビューで同じ候補が残るか確認する。