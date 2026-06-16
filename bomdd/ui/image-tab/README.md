# 画像タブ UI-IR / UI-BOM 抽出一式

ViewPrismUI(UI/UX 設計の正典 = CAD 源泉)の**画像タブモック**から、BomDD `method/ui-ir-ui-bom.md`(テーマ/思想を上位部品=designIntent として認識する拡張版)に従って抽出した観測・追跡・BOM 化用の成果物。

E-BOM の**前段**。正式品番は採番せず `TMP-UI-*` の仮品番で候補を追跡する。共有部品/概念(種別チップ・カラードット・タグ/ビュー/候補値/数値範囲)は tag-tab 抽出([../](../))の仮品番を再利用する。

| ファイル | 役割 |
|---|---|
| [ui-ir.json](ui-ir.json) | モックから抽出した中間表現(画面/領域/部品/出現/操作/入力/状態/業務概念/designIntent/対象外) |
| [ui-bom.json](ui-bom.json) | UI-IR から BOM 対象だけを昇格した候補部品表(E-BOM 連携+モック乖離の ecoImpact 候補付き) |
| [ui-trace-map.json](ui-trace-map.json) | mock locator(データバインド名/節)/ UI-IR / UI-BOM / E-BOM 候補の対応 |
| [design-intent.md](design-intent.md) | **画像タブの設計意図(上位部品)**。二軸ブラウズ・潜る/戻る統一・インライン値入力・モードペイン。tag-tab 設計意図を継承 |
| [design-system-bom.md](design-system-bom.md) | surface 部品を ECO-009 Components.axaml へマップした**カバレッジ台帳**(covered/extend/new/out-of-scope)。製造前ゲート |
| [extraction-report.md](extraction-report.md) | 抽出概要・E-BOM 連携候補・**モック乖離(差分帰属)**・read-across ギャップ |
| [unresolved-questions.md](unresolved-questions.md) | E-BOM 昇格前に maintainer 承認すべき事項(UQ-I*)。設計根幹の乖離(ナビ構造・付与インライン・グリッドサイズ)を含む |

入力モック(ViewPrismUI):
- `資料/画像タブ/ViewPrism2 画像タブ.dc.html`(M: 可変3ペイン+二軸ブラウズ+タグ編集モード)
- `docs/screens/image_tab.md`(S: 画面仕様)
- `docs/01_design_direction.md`(D: 共通 UI 方針)
- `docs/02_mock_fidelity_policy.md`(P: モック忠実度方針)

接続先 E-BOM(既存・厚い): E-UI-GRID-022(グリッド/リスト)・E-UI-TAGASSIGN-029(タグ付与パネル)・E-UI-NODEGRAPH-025(ビュー階層)・E-UI-SHELL-021(コレクション選択)・E-UI-TAGS-026/E-DOMAIN-001(タグ定義消費)・E-DESIGN-028/K-DESIGN(共有部品)。

> 関連: 方法論 `BomDD/method/ui-ir-ui-bom.md`・抽出プロンプト `BomDD/method/prompts/ui-mock-to-ui-bom.md`・接続先 [../../30-ebom.yaml](../../30-ebom.yaml)・タグタブ抽出 [../README.md](../README.md)。

## 次段(本抽出の出口)
1. [unresolved-questions.md](unresolved-questions.md) を maintainer 承認(UQ-I01/I02/I05 が設計根幹)。
2. 承認後、E-UI-NODEGRAPH-025 / E-UI-TAGASSIGN-029 / E-UI-GRID-022 / E-UI-SHELL-021 の改訂 ECO を起票。
3. [design-system-bom.md](design-system-bom.md) の `new`/`extend` を Components.axaml へ。
4. 製造(golden-in-the-loop・モック M 基準)。
