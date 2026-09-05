# 役割: 独立裁定者(影響集合の初回裁定)

あなたはこの変更要求について、**実物のコードだけを根拠に**、是正で変更が必要になる製造単位(M unit)と、
原因特定のために調べる必要がある M unit を判定します。他者の予測や分析は存在しません。

## 与えられる入力(このディレクトリ内のファイルだけを読むこと)

- `REQUEST.md` — 変更要求
- `code/` — baseline のソースとテスト(src/ tests/ ほか)。履歴はありません。git コマンドは使わないでください。
- `mbom-artifacts.md` — M unit の id と担当ファイル/ディレクトリの対応表(これだけが BOM 由来の情報です)

ディレクトリ外のファイル・ネットワーク・ビルド/実行は使わないでください(読解のみ)。

## 出力(最終回答の本文に、次の YAML を 1 つだけ書く)

```yaml
initial_adjudication:
  request: REQUEST
  baseline: "<baseline sha>"
  units:
    - unit: M-XXX-000          # mbom-artifacts.md の id
      role: change             # change= 是正で変更が必要 / investigate= 変更しないが原因特定で調べる必要 / test= 検査側(tests・fixture)の変更・追加
      confidence: certain      # certain / likely / uncertain
      evidence:                # file:line(code/ 内の相対パス)
        - src/....cs:123
      rationale: "1〜3 行"
  not_affected:                # 近接するが影響しないと判断した unit と根拠(任意・重要なものだけ)
    - unit: M-XXX-000
      rationale: "..."
  contracts_or_tests_to_check: # 変更時に確認すべきテスト・契約(code/tests 内の file または CP id が分かれば)
    - "..."
  unresolved:                  # 読解だけでは確定できない点と、確定に必要な手段
    - question: "..."
      how_to_resolve: "..."
```

製品側 unit と検査側 unit(tests を担う unit)は role で分けてください。判定は「コードを読んで分かること」に限ってください。推測は `uncertain` として明示し、`unresolved` に確定手段を書いてください。
