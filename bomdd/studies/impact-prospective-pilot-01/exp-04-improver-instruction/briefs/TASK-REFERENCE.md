# 役割: 参照表の作成者(ECO-139 / ECO-140 の構造変更が BOM に反映されるべき対応の列挙)

あなたは BOM を編集しません。2 つの ECO(order と diff)とコードから、**BOM に反映されるべき対応関係**を列挙し、
それぞれに根拠を付けた参照表を作ります。この表は、別の担当者が行った BOM 改善を採点する基準になります
(その改善の内容はあなたには与えられません)。

## 与えられる入力(このディレクトリ内のファイルだけを読むこと)

- `bom/` — 現在の BOM 一式(改善前)
- `code/` — 現在のソースとテスト(ECO-140 適用後・履歴なし。git は使わない)
- `eco/ECO-139.md`, `eco/ECO-140.md`, `eco/ECO-139.diff`, `eco/ECO-140.diff`

ディレクトリ外のファイル・ネットワーク・ビルド/実行は使わないでください。

## 作業

1. order から**設計上の変更**(品目の統合・撤去、依存・consumers の変化、契約の変化、CP/fixture の移管)を列挙する。
2. diff から**実装写像の変更**(追加・移動・削除されたファイル / クラス / メソッド / SQL・read model と、それを担う unit・品目)を
   hunk 単位で列挙する。UI だけでなくデータアクセス層・集計経路も同じ扱いで列挙する。
3. 各項目について、現在の `bom/` に**既に反映されているか / 反映されていないか**を突合し、反映されるべき場所(ファイル・id・フィールド)と
   反映されるべき内容を書く。既に反映済みの項目も列挙する(採点で「反映済みを壊していないか」を見るため)。
4. 反映**すべきでない**もの(履歴として保持すべき記録・ECO が裁定していない設計変更)も別節に列挙する。

## 出力(最終回答の本文に、次の YAML を 1 つだけ書く)

```yaml
reference:
  baseline: "79123df"
  items:
    - id: R-001
      kind: design            # design(order 由来)/ implementation-mapping(diff・code 由来)/ artifact-retirement / cp-fixture
      target: "32-mbom M-UI-REPAIR-027.artifact"     # ファイル・id・フィールド
      expected: "1〜2 行: 反映されるべき内容"
      already_reflected: false                        # 改善前の bom/ に既にあるか
      evidence:
        - "eco/ECO-140.md:78-90"
        - "eco/ECO-140.diff: RepairWindow.axaml deleted hunk"
        - "code/src/...:123"
      confidence: certain     # certain / likely / uncertain
  must_not_change:
    - target: "34-routing ROUTING-V4REPAIR-001"
      reason: "履歴ルートの記録。ECO-139/140 は routing を裁定していない"
      evidence: ["bom/34-routing.yaml:1-5"]
```

網羅より正確さを優先しつつ、diff に現れた要素は原則すべて項目化してください(反映不要なら `expected` に「反映不要」と理由)。
