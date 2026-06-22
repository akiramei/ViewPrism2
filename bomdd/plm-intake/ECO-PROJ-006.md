# ECO-PROJ-006 ViewPrism2成果物: 未解決事項ファイルに解決済み裁定が混在しています

## 宛先
- 対象リポジトリ: ViewPrism2: C:\Demo\source\repos\ViewPrism2
- 変更責務: ProjectArtifacts
- 変更種別: DefectCorrection
- 対象パス: bomdd/ui/unresolved-questions.md

## 目的
未解決キューと裁定履歴が同じ成果物に残ると、PLMは未解決件数を過大に見積もります。

## 許可スコープ
ViewPrism2側の bomdd/ 配下成果物だけを主対象にする。既存のアプリケーションソースコードは、成果物修正に必須でない限り変更しない。

## 具体作業
open-questions.yaml と decision-register.yaml へ分離するか、解決済み項目を裁定履歴へ移します。

## 関連Finding
- [Watch] 要求・裁定: 未解決事項ファイルに解決済み裁定が混在しています (bomdd/ui/unresolved-questions.md)

## 完了条件
bomdd同期後、関連する準拠Findingが解消し、PLM上で対象成果物またはBOM/工程/変更情報が構造化表示されること。

## 検証
ViewPrism2で成果物を修正後、BOM-DD PLMでbomdd同期を実行し、該当候補または関連Findingが減ることを確認する。

## 制約
- 既存成果物を削除せず、必要なら履歴・正本・registerで扱いを分ける。
- 推定で補完せず、根拠がない情報は未設定または要確認として明示する。
- 作業後はBOM-DD PLMの準拠レビューで同じ候補が残るか確認する。