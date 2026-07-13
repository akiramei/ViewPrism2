# ECO-080 — 横断関心事(i18n)の運用 3 点セット完成: XAML lint の全 View 一般化+/eco-fix 横断規約参照

- status: staged
- type: 工程ハーネス拡張(検査器+スキル手順。製品 src 不変=ECO-061/078 同型。ECO-079 教訓①の運用形)
- baseline: main ca3e065
- 発端: maintainer 工程考察(2026-07-13)=「多言語対応漏れの根本原因は、モックデザインが多言語化に触れないこと。多言語化はアプリケーションスコープの決定事項であり、横断コンテキストの置き場が必要ではないか」

## §1 要求(工程考察の合意結果)

ECO-079 の根本原因分析: モック(CAD)は画面単位の視覚原器であり、i18n のような**アプリケーションスコープの横断的決定事項**を毎画面書く場所ではない(書く設計は本質的に脆い)。横断事項は「①宣言(正本)・②作業時の参照導線・③機械ゲート」の 3 層で運用する:

| 層 | 置き場 | ECO-079 時点の状態 |
|---|---|---|
| ① 宣言 | BOM(REQ-050/051・K-AVALONIA) | **既に健全**(欠けていなかった) |
| ② 参照導線 | /eco-fix スキル手順(全製造・是正が必ず通る) | **欠落** — 手順に横断規約の適合確認がない |
| ③ 機械ゲート | lint(直書き JP=0) | **部分的** — ECO-079 の pin は ImageTabView/WorkTabView の 2 ファイル限定 |

合意した是正(maintainer 承認 2026-07-13・優先度 ③>②):
- **③ lint の全 View 一般化**: `CpI18n010XamlLintTests` の検査対象を 2 ファイル固定 → `src/**/*.axaml` 全列挙へ。将来の新画面製造時に文書参照へ依存せず機械強制になる。
- **② /eco-fix スキルへ 1 行**: 手順 2(是正の裁定)に「新規/改修サーフェスは横断規約(K-BOM の K-AVALONIA 等)との適合を確認」を追加。

CLAUDE.md への記述は補助であり本 ECO のスコープ外(②③が機能すれば必須でない — maintainer は手段に固執しない旨を明言)。

## §2 工程診断

検査器/工程ハーネスの拡張(欠陥是正ではなく予防ゲートの完成)。CAD・BOM・製品実装に欠陥なし。gate① 裁定不要(ECO-061/078 前例=ハーネス変更は設計者が進める。方針自体は上記の工程考察で maintainer 合意済み)。

## §3 切り分け済みの事実

- 全 `src/**/*.axaml`(20 ファイル)の非コメント文言属性(Text/Content/Watermark/Header/ToolTip.Tip/PlaceholderText/**Title**)に直書き日本語 **0 件**を実測(2026-07-13)= 一般化 lint は初回から緑で導入できる。Title 属性は既存 lint に無かった検査軸(全ウィンドウの Window.Title は Loc 配線済みと実測確認)。
- ECO-078 教訓「検査の暗黙前提は selftest の陽性対照として持つ」の適用: 一般化 lint が**空振りしていない**(検出器が実際に違反を見つけられる)ことの陽性対照が必要=合成した違反サンプルを検出できることを固定するテストを同梱する。
- VM 層 lint(live VM 4 ファイル)の全 VM 一般化は**スコープ外**(他 VM 群の直書き実態が未調査・棚卸しコストが別規模)。将来候補として記録。

## §4 是正方針

1. `CpI18n010XamlLintTests` の XAML lint を 2 ファイル固定 Theory → リポジトリの `src` 配下 `*.axaml` 全列挙(bin/obj 除外)へ一般化。検査属性に `Title` を追加。違反はファイル名+属性つきで全列挙報告。
2. 陽性対照テストを追加: 直書き JP 属性を含む合成 XAML 断片で検出器が違反を報告することを固定(lint の空振り防止)。
3. `.claude/skills/eco-fix/SKILL.md` 手順 2 へ横断規約参照の 1 行(K-AVALONIA=文言は Loc/T 経由・XAML 直書き禁止、を例示し K-BOM を正本として指す)。
4. CP-I18N-010 の fixture_note へ一般化 lint を追記(検査面の台帳同期)。

## §5 影響 BOM

- tests: `CpI18n010TabBindingTests.cs`(lint 一般化+陽性対照)。
- 工程ハーネス: `.claude/skills/eco-fix/SKILL.md`(1 行+根拠)。
- 台帳: `33-control-plan.yaml` CP-I18N-010 fixture_note。
- 製品 src・CAD・Oracle・DB: **変更なし**(R6 不変)。

## §6 残ゲート

- gate① 裁定: 不要(§2)。→ 是正完了(§7)。
- gate②: maintainer 実行確認(dotnet test 全緑=一般化 lint+陽性対照込み)。UI 変更なしのため実機視覚確認は不要。

## §7 実施記録(2026-07-13 /eco-fix)

### 実測裏取り(予防ゲートのため「赤」の代わりに陽性対照)

欠陥是正でなく予防ゲートの完成のため、現状で赤になるプローブは存在しない(全 20 axaml=違反 0 を起票時実測)。代わりに **陽性対照**(ECO-078 教訓の適用)で検出器の空振り不在を固定: 合成 XAML 断片(直書き JP の Text/ToolTip.Tip/Title+コメント内 JP+Loc バインド+ASCII Content)に対し、検出器が**直書き 3 件のみ**を報告することをテスト化。初回から緑=検出器が意図どおり動作する証拠。

### 是正内容

1. **lint 一般化**(`CpI18n010XamlLintTests`): 2 ファイル固定 Theory → `src` 配下 `*.axaml` 全列挙 Fact(bin/obj 除外・列挙数 ≥20 のガードで列挙自体の空振りも防止)。検査属性に `Title` を追加(全ウィンドウの Window.Title も検査軸へ=実測で全 View 配線済みのため初回緑)。違反はファイル名+属性つきで全列挙報告。検出ロジックは `HardcodedJapaneseInXaml(string)` へ分離(陽性対照と共有)。
2. **陽性対照テスト追加**(上記)。
3. **/eco-fix スキル手順 2 へ横断規約参照**: 「新規/改修サーフェスは K-BOM の横断規約(K-AVALONIA 等)との適合を確認。モックに書いていない≠規約がない(ECO-079 実績)」を追記。
4. **CP-I18N-010 台帳同期**: fixture_note へ一般化 lint+陽性対照を追記、test_vectors へ ECO-080 行を追加。

### 機械受入(4 点・全緑)

- `dotnet build ViewPrism2.sln`: 0 warning / 0 error。
- Tests: **664/664**(旧 2 ファイル Theory 2 本 → 全 View Fact+陽性対照 Fact の 2 本=総数不変)。
- Oracle: 109 pass / 2 known skip(R6 不変)。
- `python bomdd/validate_bom.py`: 0 error / 0 warning。

R7 セルフゴールデン: 対象外(UI サーフェス不変・製品 src 不変)。

**次 gate=②**(maintainer 実行確認)。
