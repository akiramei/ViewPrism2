# 役割: BOM 改善者(ECO-139 / ECO-140 の反映点検)

このプロジェクトは BomDD(E-BOM / M-BOM / Control Plan / Routing で設計と製造を管理)で運用されています。
直近の 2 つの変更 ECO-139・ECO-140 は、未裁定(pending)裁定面と修復面を「事象中心の統合裁定面」へ再設計し、
relink を一本化し、旧 Window を撤去するなど構造を変えました。**その構造変更が BOM に正しく反映されているか**を
点検し、陳腐化・欠落している写像を訂正してください。

## 与えられる入力(このディレクトリ内のファイルだけを読むこと)

- `bom/` — 現在の BOM 一式(10-requirements / 20-spec / 30-ebom / 31-kbom / 32-mbom / 33-control-plan / 34-routing / 53-service-bom)
- `code/` — 現在のソースとテスト(ECO-140 適用後の状態)。履歴はありません。git は使わないでください。
- `eco/ECO-139.md`, `eco/ECO-140.md` — 2 つの ECO の change order(裁定・設計・実施記録)
- `eco/ECO-139.diff`, `eco/ECO-140.diff` — 2 つの ECO の実 diff(src / tests / BOM)

ディレクトリ外のファイル・ネットワーク・ビルド/実行は使わないでください。

## 作業

1. ECO-139/140 の order と diff から、設計上の変更(品目の統合・撤去、依存の変化、契約の変化、artifact の移動・削除)を列挙する。
2. `bom/` の各台帳がそれを反映しているかを突合し、**`bom/` 配下のファイルを直接編集**して訂正する。
   - 対象: 30-ebom(品目・depends_on・graph_edges の consumers・rationale の superseded 表記)、32-mbom(unit・ebom_refs・
     artifact path / path_note・interface_contract)、33-control-plan・34-routing(必要な場合のみ)。
   - 削除済みファイルを指す artifact は、削除の事実と統合先を path_note に残したうえで superseded と分かる形にする。
3. **設計上の対応と、実装からの観測を区別する**:
   - ECO の order(裁定・設計)に根拠がある編集 → 通常の設計フィールド(depends_on / consumers / contract 等)へ。
   - コードを読んで初めて分かった対応(どのファイル・メソッドがその品目を担うか)→ `path_note` や contract の
     観測欄に **`(observed@79123df)`** を付けて書く。設計を書き換えない(実装が設計と食い違う場合は CHANGELOG に
     「実装の逸脱の疑い」として記録し、BOM 側を実装に合わせて書き換えない)。
4. `CHANGELOG.md` を作成し、編集ごとに次を 1 項目で記録する:
   `ファイル / 編集箇所(id) / 種別(陳腐化訂正・欠落追加・契約更新・観測追記)/ 根拠(eco/ の order 行・diff の hunk・code の file:line)`。
   **根拠を書けない編集はしない。** 改善対象の判断に迷ったら、編集せず CHANGELOG の「未編集・要裁定」に理由つきで残す。

## 制約

- 触ってよいのは `bom/` と `CHANGELOG.md` だけ。`code/` と `eco/` は読み取り専用。
- 新しい品目・unit の新設は最小限にし、既存 id の意味を変えない。id の改名はしない。
- 網羅より正確さを優先する。根拠のある編集だけを残す。
