# タグタブ UI-IR / UI-BOM 抽出一式

HTML+JS+CSS で作られた **タグタブのUI/UXモック**(`work/tag-tab/`)から、BomDD `method/ui-ir-ui-bom.md` に従って抽出した観測・追跡・BOM 化用の成果物。

E-BOM の **前段**。正式品番は採番せず `TMP-UI-*` の仮品番で候補を追跡する。既存 E-BOM(E-UI-TAGS-026 / E-UI-NODEGRAPH-025 / E-EVAL-002 / E-TAGSVC-008 / E-VIEWSVC-009 / E-GRAPH-003 / E-DESIGN-028 / E-DOMAIN-001 ほか)への **接続候補** を持つ。

| ファイル | 役割 |
|---|---|
| [ui-ir.json](ui-ir.json) | モックから抽出した中間表現(画面/領域/部品/出現/操作/入力/状態/業務概念/対象外) |
| [ui-bom.json](ui-bom.json) | UI-IR から BOM 対象だけを昇格した候補部品表(E-BOM 連携付き) |
| [ui-trace-map.json](ui-trace-map.json) | HTML locator / UI-IR / UI-BOM / E-BOM 候補の対応 |
| [extraction-report.md](extraction-report.md) | 抽出概要・対象/対象外理由・E-BOM 連携候補・read-across ギャップ |
| [unresolved-questions.md](unresolved-questions.md) | 仕様/UI 設計/E-BOM 昇格前に確認すべき事項 |
| [visual-gap-tag-tab.md](visual-gap-tag-tab.md) | 製造品(実機)を CAD(モック+UI-IR)と視覚突合した製造検査。設計言語の乖離全量(ECO-009 の入力) |
| [design-intent.md](design-intent.md) | **設計意図の正典**。モック DESIGN DIRECTION 01/02/03 から抽出(思想・4原則・トークン・IBM Plex)。初回 nonBom 誤分類を是正・K-DESIGN/ECO-009 の基盤 |

入力モック:
- `work/tag-tab/ViewPrism2 タグ管理.dc.html`(M1: 3ペイン全体)
- `work/tag-tab/ViewPrism2 タグ作成ダイアログ.dc.html`(M2: 適応型モーダル)
- `work/tag-tab/ViewPrism2 配置タグ設定.dc.html`(M3: 階層フル版+条件ダイアログ)
- `work/tag-tab/ViewPrism2 タグタブ仕様書.dc.html`(S: 機能説明/抽出根拠)

関連:
- 方法論: `BomDD/method/ui-ir-ui-bom.md`
- 抽出プロンプト: `BomDD/method/prompts/ui-mock-to-ui-bom.md`
- 接続先 E-BOM: [../30-ebom.yaml](../30-ebom.yaml)

> 注意: 入力モックは `work/`(.gitignore 対象)に置かれている。本 `bomdd/ui/` 成果物は追跡対象。モックを版管理したい場合は別途方針を要決定(unresolved 参照外)。
