---
name: eco-fix
description: 起票済み ECO の是正実施。プローブ先行(是正前不合格の実測裏取り)→最小是正→機械受入→セルフゴールデン(R7・UI fix は CAD captures 並置で転写漏れ 0)→fix コミットまで行い、golden 合格基準(human gate)を提示して停止する。起票(/eco-file)がまだの案件には使わない。
---

# /eco-fix — プローブ先行の是正+機械受入

典拠: [bomdd/change-management.md](../../../bomdd/change-management.md) §3.3。
引数: ECO 番号(例: `eco-038`)。

## 前提確認

1. `bomdd/60-change-order-eco-NNN.md` と register エントリが存在し、工程診断が
   「実装層」(または合意済みの是正対象)を指していること。無ければ /eco-file へ差し戻す。
2. 同一ファイルに触る進行中の ECO 系列がないか register で確認(diff 混濁回避 — ECO-037 §3)。

## 手順

1. **プローブ先行(R5)**: 是正**前**に、症状を固定する回帰テストを追加する
   (既存の CP テストクラスに追記。例: CpUiG1WorkTabTests)。実行して
   **不合格になることを確認**= 真因の実測裏取り。
   - **プローブが不合格にならない場合、診断が誤り。コードに触らず /eco-file の工程診断へ差し戻す。**
   - **UI サーフェスの新設/視覚変更を含む場合**: 視覚 probe(headless 実レイアウト)は
     CAD の**視覚契約チェックリスト**(ViewPrismUI `docs/screens/<screen>.md` の visualContract 節)
     から**この時点で生成**する(GF 後追い禁止 — GF-073-01〜07 の様式)。チェックリスト未整備の
     legacy 面に触れる場合は、先に CAD 側でチェックリストを生成してから着手(lazy 遡及)。
2. **是正の裁定**: ECO 本文 §4 の案から選ぶ。選定基準は「**真因構造そのものを消す案を優先**」
   (ECO-038: 通知 2 行の対症でなく全通知化で手書きリスト構造を廃した)。最小 diff・
   既存 golden の視覚不変を尊重。コメントは既存流儀(ECO 番号+制約の理由)で。
   - **横断規約の適合確認(ECO-080)**: 新規/改修サーフェスは、モック(CAD)が沈黙する
     アプリケーションスコープの横断規約(正本= `bomdd/31-kbom.yaml` の K-AVALONIA 等。
     例: 文言は LocalizationService/Loc 経由・XAML/VM 直書き禁止= REQ-050/051)との適合を
     この時点で確認する。**モックに書いていない≠規約がない**(ECO-079: i18n 未配線が
     2 面×2 層で潜伏した実績)。
3. **機械受入(4 点)**: `dotnet build`(0 error)/ `dotnet test tests/ViewPrism2.Tests`(全緑・
   プローブも合格に転じること)/ `dotnet test tests/ViewPrism2.Oracle` / `python bomdd/validate_bom.py`(0-0)。
   **R6: 既存固定オラクル行は変更しない。** 期待値改訂が必要になったら停止して報告(挙動保存の破れ)。
3.5. **セルフゴールデン(R7・UI サーフェスに触れた fix のみ)**: golden 提示の**前**に、
   是正対象の各サーフェスを CAD captures と並置する(headless レンダリング=
   Avalonia.Headless の CaptureRenderedFrame 等、または実機スクリーンショット)。
   差分を**全列挙**して「裁定済み許容差分(裁定記録を引用)/転写漏れ」に分類し、
   **転写漏れ 0 になるまで golden に出さない**。共通言語(ViewPrismUI
   `docs/03_dialog_language.md`)に触れた場合は、直した面だけでなく**適用面マトリクスの
   該当列の全面**を並置する(GF-073 系=同一言語の転写漏れが面を変えて 4 回連鎖した実績)。
   並置結果(検査した面・差分の分類)は手順 4 の実施記録へ含める。
4. **記録**: ECO 本文へ実施記録節(実測裏取り・裁定理由・diff 規模・機械受入結果)を追記し、
   §残ゲートを更新。register の status を `implemented` へ(注記=「是正+機械受入完了・golden 待ち」)。
5. **コミット**: `fix(eco-NNN): <要約>`(機械受入結果を本文に含める)。
   **ライフサイクル trailer 必須(ECO-061)**: staged→implemented の遷移コミットには
   `BomDD-ECO-Fix: ECO-NNN` trailer を携行する(commit-msg hook が fail-closed で強制):
   ```
   git commit -m "fix(eco-NNN): <要約>" -m "BomDD-ECO-Fix: ECO-NNN"
   ```

## 停止点(human gate② = golden)

**golden 合格基準を操作手順つきの箇条書きで提示して停止する。** 例(ECO-038):
「作業タブで画像のあるスペースを開き、グリッド⇔リスト押下で本体が即時切替(往復)・
ボタン active 状態と本体表示が常に一致」。
共有コンテナ/共有 VM を触った場合は**非表示状態(条件付き IsVisible の裏面)の再検査**も
基準に含める(ECO-037 教訓)。

合格の報告を受けたら /eco-accept eco-NNN へ。不合格所見(GF-*)が出たら本スキルの手順 1 から
(所見が別欠陥なら R3: 分離起票)。

## スコープ外所見(R3)

是正中に見つけた別問題は現 ECO の diff に混ぜない。分離起票(/eco-file)か 51-cheat-log 記録の二択。
