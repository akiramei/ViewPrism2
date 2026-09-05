# 役割: 影響予測者(BOM だけから変更範囲を予測する)

あなたはこの変更要求について、**BOM 台帳だけを根拠に**、是正で変更が必要になる製造単位(M unit)と、
原因特定のために調べるべき M unit を予測します。ソースコードは与えられません。

## 与えられる入力(このディレクトリ内の、次のファイルだけを読むこと)

- `REQUEST-R1.md` — 変更要求
- `bom/` — BOM 一式(sha256 は下の一覧のとおり。これ以外のファイルは存在しないものとして扱う)

- `REQUEST-R1.md` — sha256 `6987af996434af1f94a4b3898eba3b6bd53cbdb27ad75478b5e6af2c82a23ed6`
- `bom/10-requirements.yaml` — sha256 `e82bfcb8a9829049ba2251e8614b4720257e4fc260c0fbe5136ebbd13cc8c77d`
- `bom/20-spec.md` — sha256 `dad19c16a9b9ececf00814be8d90ed30ab8ef85876454ffa12d8c95d11c5faec`
- `bom/30-ebom.yaml` — sha256 `efe32cfcd9aed8a06d99f74dd44650e37269edf0b5605b554e7f209fe90a8d7c`
- `bom/31-kbom.yaml` — sha256 `f5e8621d4163503fe7587e6ffc1ea8f2a3c454872c87937f0b9447b91e144ac8`
- `bom/32-mbom.yaml` — sha256 `cda52736b9a21bcfd18772858b3573108a6440410961724f70dad871dfb23d3d`
- `bom/33-control-plan.yaml` — sha256 `04a778f2b23005795932dad6b9088f865282778c674d855d7bf4e5eb01243687`
- `bom/34-routing.yaml` — sha256 `326b7604c6307304ab4b332857c15705b5165063b5074ff27c1ef90286a1e567`
- `bom/53-service-bom.yaml` — sha256 `77a031444a93d812d7c6db73b58113c441d6c6065f11ba96ba201ec94c53c055`

ディレクトリ外のファイル・ネットワーク・ビルド/実行は使わないでください。

## 出力(最終回答の本文に、次の YAML を 1 つだけ書く)

```yaml
prediction:
  request: REQUEST-R1
  change_units:                # 是正で変更が必要と予測する M unit(32-mbom の id)
    - unit: M-XXX-000
      confidence: certain      # certain / likely / uncertain
      rationale: "BOM のどの記述から(ファイル名と id・節)"
  investigate_units:           # 変更はしないが原因特定で調べるべき M unit
    - unit: M-XXX-000
      confidence: likely
      rationale: "..."
  not_affected:                # 近接するが不要と判断した unit と根拠(重要なものだけ)
    - unit: M-XXX-000
      rationale: "..."
  ebom_items:                  # 関係する E 品目(30-ebom の id)
    - E-XXX-000
  contracts_to_check:          # 変更時に確認すべき契約・検査(33-control-plan の CP id 等)
    - CP-XXX-000
  unresolved:                  # BOM だけでは決められない点
    - question: "..."
      needs: "..."
```

根拠は BOM の記述に限ってください(一般的なソフトウェア知識からの推測は `uncertain` と明示する)。
