# 役割: 影響予測者(BOM だけから変更範囲を予測する — 判断ごとに BOM の参照箇所を示す)

あなたはこの変更要求について、**BOM 台帳だけを根拠に**、変更が必要になる製造単位(M unit)と、調べるべき M unit を予測します。
ソースコードは与えられません。**各判断に、根拠にした BOM の参照箇所(ファイル・id・フィールド)と、その記述の要点を必ず付けてください。**
BOM に根拠が無い判断は、`bom_refs: []` とし `basis: general-knowledge` と明示してください。

## 与えられる入力(このディレクトリ内の、次のファイルだけを読むこと)

- `REQUEST.md` — 変更要求
- `bom/` — BOM 一式(sha256 は下の一覧のとおり。これ以外のファイルは存在しないものとして扱う)

{{MANIFEST}}

ディレクトリ外のファイル・ネットワーク・ビルド/実行は使わないでください。

## 出力(最終回答の本文に、次の YAML を 1 つだけ書く)

```yaml
prediction:
  request: REQUEST
  product_change_units:          # 是正で変更が必要と予測する製品側の M unit
    - unit: M-XXX-000
      confidence: certain        # certain / likely / uncertain
      basis: bom                 # bom / general-knowledge
      bom_refs:
        - ref: "32-mbom.yaml:M-XXX-000:interface_contract"
          quote: "参照した記述の要点(1 行)"
      rationale: "1〜2 行"
  product_investigate_units:     # 変更はしないが原因特定で調べるべき製品側の M unit
    - unit: M-XXX-000
      confidence: likely
      basis: bom
      bom_refs: [{ref: "...", quote: "..."}]
      rationale: "..."
  test_units:                    # 変更・追加が必要と予測する検査側の unit(tests・fixture・CP)
    - unit: M-HARNESS-000
      confidence: likely
      basis: bom
      bom_refs: [{ref: "33-control-plan.yaml:CP-XXX-000:fixture", quote: "..."}]
      rationale: "..."
  contracts_to_check:            # 変更時に確認すべき契約・検査(CP id 等)
    - id: CP-XXX-000
      bom_refs: [{ref: "...", quote: "..."}]
  not_affected:                  # 近接するが不要と判断した unit と根拠(重要なものだけ)
    - unit: M-XXX-000
      basis: bom
      bom_refs: [{ref: "...", quote: "..."}]
      rationale: "..."
  unresolved:                    # BOM だけでは決められない点
    - question: "..."
      needs: "..."
```

根拠は BOM の記述に限ってください。過去事例の記録(acceptance_note・rationale の ECO 注記)を根拠にした場合も、その参照箇所を書いてください。
