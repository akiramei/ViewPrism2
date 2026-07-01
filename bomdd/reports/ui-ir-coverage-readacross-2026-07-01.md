# UI-IR カバレッジ 横展開監査(CAPA read-across)

日付: 2026-07-01
契機: ECO-022 マッピングモーダルが golden で作り直しになった工程欠陥(mock→UI-IR 未取込のまま製造→BOM がクローム未仕様→golden が設計工程化。FMEA-037・§10・[[mock-ui-ir-is-cad]])。
目的: 同クラスの欠陥(UI-IR を持たず prose/原典由来で製造した surface)が他にも無いかを横展開で洗い出し、優先度を付ける。ECO-003→ECO-004 の read-across と同型の CAPA。

## 方法

ViewPrism2 の全 UI surface を、**2 つの UI-IR ロケーション**に照らして突合した:

- `bomdd/ui/`(元の UI-IR/UI-BOM 抽出。tag-tab 上位 + image-tab/ + work-tab/)
- `ViewPrismUI/docs/screens/`(新 CAD リポの正規化スクリーン仕様)+ `ViewPrismUI/資料/`(一次モック)

各 surface について「mock→UI-IR→BOM を辿ったか / prose・原典から直製造か」を判定し、リスク(次の変更で golden が設計工程化するか)を評価。

## カバレッジ表

| surface | 製造 ECO | bomdd/ui IR | ViewPrismUI IR | 一次モック | 製造由来 | リスク |
|---|---|---|---|---|---|---|
| タグタブ | ECO-007 他 | ✓(上位) | ✓ `tag_tab.md` | `資料/タグタブ/` | mock→IR→BOM | 低 |
| 画像タブ(browse/tag編集/整理) | ECO-010〜018 | ✓ `image-tab/` | ✓ `image_tab.md` | `資料/画像タブ/` | mock→IR→BOM | 低 |
| 作業タブ | ECO-020/021 | ✓ `work-tab/` | ✓ `work_tab.md`(2026-07-01) | ✓ `資料/作業タブ/`(2026-07-01) | mock→IR→BOM + CAD 統合済 | ~~中~~→済 |
| **画像ビューアー(全体)** | phase1(非ECO)+ ECO-022 | **✗ 皆無** | △ タグ制御のみ(`image_viewer_tag_control.md`) | `資料/ビューア/`(2026-07-01 取込) | **prose §2.9/§2.12 + Downloads モック直** | **高** |
| legacy モーダル群<br>(修復/トラッシュ/設定/フォルダ管理/タグ編集/類似検索/マージ/数値/ノード条件) | 原典ポート | ✗ | ✗ | ✗(原典由来) | 原典 view-prism ポート | 低〜中(方針次第) |

## 所見(優先度順)

### P1(高・最重要): 画像ビューアーに UI-IR が皆無

- **事実**: ビューア(ツールバー/セグメントモードピル/単一・縦スクロール・右開き・左開きの4モード/設定ドロワー/下部バー/キャンバス)は **spec prose(§2.9/§2.12)+ Downloads の standalone モック直**で製造。`bomdd/ui/` にビューア IR は存在しない。ViewPrismUI にも、今回 ECO-022 で作った**タグ制御部分のみ**(`image_viewer_tag_control.md`)。
- **含意**: ECO-022 マッピングモーダルの「golden が作り直し」は**症状の1つ**にすぎず、ビューアの非タグ制御部分(モード別設定ドロワー・4モード描画・下部バー)も同じ prose-only 状態。次にビューアの視覚を変更するとき、また golden が設計工程化する。
- **CA**: ビューア全体の UI-IR を抽出し ViewPrismUI へ取込(`docs/screens/image_viewer.md` へ拡張 or 新設。`資料/ビューア/` の一次モックは取込済)。golden CP は CP-UI-G11 を「タグ制御のみ」からビューア全体のクローム突合へ広げるか、ビューア用 golden CP を新設。
- **状態(2026-07-01 実施)**: **`ViewPrismUI/docs/screens/image_viewer.md` を新設**(モック権威・4モード/ツールバー/設定ドロワー/下部バー・タグ制御は `image_viewer_tag_control.md` サブ参照)。突合で golden 実装の新規乖離 **V1〜V5** を検出: V1 ドロワー幅 376 vs 360 / **V2 ドロワーのスクリム有無** / **V3 ドロワー内ヘッダ有無** / **V4 ツールバー「タグ制御 ON」バッジ有無** / V5 タイトルバー(=ネイティブ窓・許容)。**V2/V3/V4 は UX 大差分=追随 or 現行維持の maintainer 裁定待ち**(レビュー項目)。実装追随は未着手(GF-TAGCTRL-05 と併せて別 ECO)。

### P2(中): 作業タブが ViewPrismUI 未統合 → ✅ **是正済(2026-07-01)**

- **事実**: `bomdd/ui/work-tab/` に UI-IR/UI-BOM はある(ECO-020/021 はそこから製造=mock→IR→BOM を辿っている)が、**ViewPrismUI/docs/screens に未 migration**(索引「作業タブ=未作成/未提供」)。一次モック `作業タブ.html` も `資料/` 未配置(cad_input「取込前=Downloads」)。
- **含意**: IR は存在するので「golden が設計工程化」リスクは P1 より低い。ただし CAD リポの一次仕様が二重管理(bomdd/ui と ViewPrismUI)で不完全。
- **CA**: `作業タブ.html` を `資料/` へ配置 + `bomdd/ui/work-tab/` を `docs/screens/work_tab.md` へ正規化統合。
- **是正実施(2026-07-01)**: 一次モック `ViewPrism2 作業タブ.html`+`ViewPrism2 作業タブ削除.html` を `ViewPrismUI/資料/作業タブ/` へ配置。`ViewPrismUI/docs/screens/work_tab.md`(モック権威・extraction-report + ECO-020/021 裁定を正規化)を新設。索引(integrated_ui_spec.md 画面別仕様表・シェル節・README)更新。ゴミ箱文言は INV-009 是正を明記。**残=P3 の権威統一(bomdd/ui/work-tab を退役 or 参照専用にするか)**。

### P3(中): UI-IR ロケーションが 2 系統に分裂

- **事実**: tag-tab/image-tab/work-tab の IR は `ViewPrism2/bomdd/ui/`、新 CAD は `ViewPrismUI/docs/screens/`。移行が中途(image-tab/tag-tab は両方にあり、work-tab は bomdd/ui のみ、viewer は ViewPrismUI のみ)。
- **CA**: どちらを正の CAD とするか方針決定(ViewPrismUI が新方針=[[viewprismui-cad-repo]])+ 未移行分(work-tab)を寄せる。移行完了まで「どちらが権威か」を各 surface で曖昧にしない。

### P4(低〜中): legacy モーダル群は mock CAD 非対象

- **事実**: 修復/トラッシュ/設定/フォルダ管理/タグ編集/類似検索/マージ/数値入力/ノード条件の各ダイアログは**原典 view-prism ポート**で、mock CAD の対象外。表示パリティ契約は ECO-003 で修復候補カードのみ明文化(FMEA-031)。
- **含意**: これらは「モック準拠設計」の射程外(原典準拠が意図)なので、現時点で欠陥ではない。ただし maintainer が将来これらもモック化するなら、IR 無しの未カバー領域として残る。
- **CA**: 方針判断(原典準拠を維持する surface と、モック化する surface を明示的に線引き)。当面は「原典準拠=意図的」と記録するだけで可。

## 推奨する次アクション

**P1(ビューア全体の UI-IR 抽出)を最優先**とする。理由: 直近で実際に噛んだ surface であり、タグ制御部分だけ IR 化しても残りが prose-only のまま=再発リスクが最も高い。P2/P3 は IR が存在する分リスクが低く、統合作業として後続で可。P4 は方針判断のみ。

## 関連

- 契機: ECO-022 §10・GF-TAGCTRL-05・FMEA-037・CP-UI-G11 拡張。
- 前例: `bomdd/reports/display-parity-readacross-2026-06-15.md`(ECO-003→ECO-004 の表示パリティ read-across)。
- 原則: [[mock-ui-ir-is-cad]]・[[viewprismui-cad-repo]]。
