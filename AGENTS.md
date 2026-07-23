# AGENTS.md — エージェント向け入口(ハーネス中立)

このリポジトリは **BomDD(BOM 駆動開発)管理下**です。あなたのハーネスに
スラッシュコマンド(`/eco-file` 等)が表示されなくても、以下の手順書が正本です —
**該当する SKILL.md を読み、記載どおりに実行してください**。自由文プロンプトから
直接コードに入る経路は存在しません。

## 最初に読む

- 変更管理の正本: [bomdd/change-management.md](bomdd/change-management.md)
  - **R1: 起票なき src/tests 変更の禁止**(すべての変更は ECO から)
  - R2: コードから入らない(所見はまず工程診断 — CAD/BOM/実装のどこの欠陥か)
  - R3: ついで修正禁止(スコープ外所見は分離起票か 51-cheat-log 記録)
- 運用宣言の全体: [CLAUDE.md](CLAUDE.md)(Claude Code 向けアダプタ —
  本ファイルと同じ正本群を指す。手順の実体はどちらにもない)

## 入口手順(単一正本 = 各 SKILL.md。ここには複製しない)

| 用途 | 手順書 |
|---|---|
| ECO 起票+工程診断(不具合・新機能・拡張すべての入口) | [.claude/skills/eco-file/SKILL.md](.claude/skills/eco-file/SKILL.md) |
| プローブ先行の是正+機械受入 | [.claude/skills/eco-fix/SKILL.md](.claude/skills/eco-fix/SKILL.md) |
| golden 合格後のクローズ | [.claude/skills/eco-accept/SKILL.md](.claude/skills/eco-accept/SKILL.md) |
| OSS 脆弱性の実測逆引き | [.claude/skills/sec-advisory/SKILL.md](.claude/skills/sec-advisory/SKILL.md) |

## 台帳と機械検査

- 変更台帳: [bomdd/60-change-register.yaml](bomdd/60-change-register.yaml)
- 整合性検査: `python bomdd/validate_bom.py`(0 error が納品条件)
  - **E14〜E19 = 台帳状態遷移 × git 履歴証拠**。fix/accept の遷移コミットは
    trailer(`BomDD-ECO-Fix:` / `BomDD-ECO-Accept:`)を携行する — `bomdd/hooks/commit-msg`
    が強制する。詳細は各 SKILL.md と change-management.md §4。

## human gate(人間の作業は 2 種類)

1. **gate① 裁定**(方針・要求・候補の裁定 — 選択肢から選ぶ)
2. **gate② golden**(成果物の golden 承認)

gate② は**成果物ごとに複数回発生し得る**。UI 変更 ECO では **CAD/mock golden** と
**実機 golden** を gate② の別インスタンスとして扱う。

gate に到達したら**停止**し、「人間がやること」を明示して待つこと。自然文の了承を
gate 通過や次工程の実行指示に**昇格させない**(実行は明示のコマンド/指示のみ)。

## 関連リポジトリ

- UI/UX 設計原器(CAD): `../ViewPrismUI`(乖離時は常に CAD が正)
- 方法論: `../BomDD` / PLM 工具: `../BomDD-Plm`
