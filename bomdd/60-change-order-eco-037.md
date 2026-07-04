# Change Order — ECO-037(起票・staged): 整理モードのマージ完了パネルが実機で表示されない

> ECO-036 第3段の golden 所見の切り分け(51 G-E36S3)で**既存バグと確定**した実害の是正。
> 検出経緯: 切り出しの挙動保存検証(実機 A/B)が既存バグを露出させた — 変更前個体(dc990ef)でも再現。

## 1. 症状(maintainer 実機・2026-07-04)
- 整理モードでマージ実行 → データは正常(source deleted・タグ union・件数減)だが、
  **右ペインに完了パネル(「統合しました」+DoneSummary+「別の整理を続ける」)が出ず空白**になる。
- 帰結: 続行ボタンが存在しないため「別の整理」が継続できない(整理を終了→再進入で回避可能)。

## 2. 切り分け済みの事実(51 G-E36S3)
- VM は健全: マージ後 OrganizeDone=true・DoneSummary 正・PropertyChanged(string.Empty)到達(プローブ実測)。
- XAML 表示条件は `IsVisible={Binding OrganizeDone}`(302 行)で単純。
- 疑い(未検証): ①右ペイン DockPanel の子配置 — 完了 ScrollViewer が DockPanel.Dock 無指定
  (非終端子= Dock.Left 既定)で幅算定が壊れる ②ECO-014 golden 以降の右ペイン改変 ECO
  (ECO-021/025 系)での退行 ③Width="{Binding $parent[ScrollViewer].Viewport.Width}" の自己参照。
- 完了パネルの golden 実績: ECO-014 受入時のみ(以後の右ペイン改変 ECO は完了状態を再検査していない
  = read-across 漏れの可能性 — ECO-004 の readacross_lesson と同根)。

## 3. 方針(着手時に確定)
- 挙動変更(バグ修正)につき ECO-036 系列(挙動不変)とは独立に実施。修正後、完了状態の
  再発防止として golden 観点へ「マージ完了パネル」を明記。
- 着手条件: ECO-036 系列と同一ファイル(ImageTabView.axaml)に触るため、**系列第4段より前に単独実施**
  が望ましい(同時変更の diff 混濁回避)。

## 4. 真因と是正(2026-07-04 実施 — 機械受入完了・golden 待ち)

- **真因(履歴で確定)**: §2 の疑い①+③の複合。導入時(51ad8ee・ECO-014)は幅バインドなしで golden 合格
  → **1d93378(golden GF 是正「右ペイン幅はみ出し」)が StackPanel の Viewport 幅バインドを追加** →
  トレイ(DockPanel 最終子= 残域いっぱい・幅 344 確定)は無事だが、完了パネルは **Dock 無指定の
  非終端子(Dock.Left 既定)**のため「ScrollViewer 幅⇔コンテンツ幅⇔Viewport 幅」の自己参照で幅 0 に潰れた。
  以後、完了パネルは一度も視覚検査されず潜伏(read-across 漏れ — ECO-004 readacross_lesson と同根)。
- **是正**: 完了/トレイの 2 ScrollViewer を `Panel` で包み DockPanel の最終子にする(幅 344 確定・
  IsVisible の排他切替は不変)。diff = ImageTabView.axaml **+6 行のみ**(真因コメント 4+Panel 開閉 2)。
- 機械受入: build 0・Tests 526/526・Oracle 100/102・lint 0(VM/tests/oracle 無変更)。
- **残ゲート= golden(maintainer 実機)**: マージ実行 → **完了パネル(「統合しました」+DoneSummary+
  「別の整理を続ける」)が右ペインに出る** → 続行 → トレイに戻り 2 周目のマージ先設定ができる。
  合格時: 本 ECO applied+golden 観点へ「マージ完了パネル」を明記(再発防止・§3)。

## 5. クローズ(2026-07-04 golden 合格)

- maintainer 実機: 完了パネル(「統合しました」+DoneSummary+「別の整理を続ける」)表示 OK・
  続行→トレイ復帰→2 周目マージ先設定 OK(スクリーンショット確認)。
- 再発防止: **CP-UI-G9 に「マージ完了パネル」観点を明記**(導入後の右ペイン改変で不可視化した実績つき)。
- 教訓(read-across の view 版): 共有コンテナ(DockPanel/レイアウト)を触る golden 是正は、
  **同コンテナ内の非表示状態(条件付き IsVisible の裏面)も再検査対象**に含める — 表示中の面だけの
  目視では裏面の崩壊(幅自己参照等)を見逃す。ECO-004 readacross_lesson のレイアウト版。
