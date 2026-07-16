# Change Order — ECO-101(implemented): タグタブ操作系の入力堅牢性 — ポインター押下状態の残留・右クリック配置・無変更ホーム設定の dirty 化

- 起票: 2026-07-17(maintainer ソースレビュー所見 3 件・未 push 12 コミットのレビュー)
- 種別: 不具合(入力イベント処理の堅牢性欠落。ECO-099/100 混入・golden 手順の谷間で潜伏)
- baseline: main `f598b3e`

## 1. 症状(報告・2026-07-17 maintainer レビュー。2026-07-17 コード実測で全件確認済み)

同一サーフェス(タグタブ中央/右ペイン)の入力処理欠陥 3 件:

1. **[P2] ポインター押下状態の残留**: パレットカード(`TagsTabView.axaml.cs:98` OnPaletteItemPressed)と
   階層行(`:292` OnNodeRowPressed)は press 状態(`_palettePressRow`/`_rowPressNode` 等)を記録するが、
   `Pointer.Capture` を明示せず・`PointerCaptureLost` を処理せず・`OnPaletteItemMoved`/`OnNodeRowMoved` は
   **ボタン押下状態を実測確認しない**。Avalonia の暗黙キャプチャにより通常の「領域外リリース」は
   `PointerReleased` が押下元へ届くため顕在化しないが、**キャプチャ喪失系**(押下中のウィンドウ非活性化・
   他コントロールの Capture 奪取・タッチキャンセル・D&D 開始による喪失)では Released が届かず状態が残留 →
   以後の**ボタン無しホバー移動**で閾値判定が成立し、失効した press 引数で `DoDragDropAsync`
   (=誤ドラッグ・ツリー変更に到達し得る)が発火する。
2. **[P2] 右クリックで配置が確定する**: 挿入ポイント 3 種+「＋ 子にする」の 4 ハンドラ
   (`TagsTabView.axaml.cs:351-385` OnInsertBeforePressed/OnInsertChildEndPressed/OnInsertRootEndPressed/
   OnMakeChildPressed)がボタン種別未確認で `PointerPressed` 即実行。配置モード中の右クリック(中ボタンも)で
   階層が変更される。同ファイルの行(`:292`)・カード(`:101`)・ビュー行(`:90`)は
   `IsLeftButtonPressed` 確認済み= この 4 つだけの欠落。
3. **[P3] 現ホーム行への「ホームに設定」が無変更で dirty 化**: `HierarchyEditorViewModel.cs:572`
   `SetHomeFromMenu` は変更有無を見ず `SetDirty(true)`。既にホームの行で実行すると実変更なしで
   保存/キャンセルが活性化し、ビュー切替の破棄確認・保存時 `modified_at` 更新が発生する
   (ゴースト経路 ToggleHome は常に実変更=問題なし)。

## 2. 工程診断 — CAD/BOM 沈黙(入力堅牢性=アプリ横断規約の層)・実装欠陥と確定。gate① 不要

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | **沈黙(欠陥ではない)** | mock/tag_tab.md は右クリック・キャプチャ喪失・無変更 dirty を規定しない(ブラウザプロトタイプに存在しない次元)。「クリック=左ボタンの主操作」「実変更のみ dirty」は暗黙の横断品質。 |
| BOM | **健全(検査の谷間は追補対象)** | E-UI-NODEGRAPH-025/CP-UI-G6 の ECO-099/100 次元は左クリック正常系のみ。右クリック・キャプチャ喪失は golden 手順にも機械 probe にも無かった=谷間(fix 時 CP へ追補)。 |
| 実装 | **欠陥と確定(変更対象)** | §1 の各行番号で実測確認済み。 |

- **混入コミット**: 症状 1(パレット面)・2・3= ECO-099 `e8bb277`(2026-07-16)。症状 1(行面)=
  ECO-100 `7ef1a00`(同日・行 D&D 追加で対象拡大)。潜伏 1 日・未 push。
- **マスキング要因**: golden(ECO-099/100 とも approved)は左クリック正常系+Esc キャンセルのみを
  検査手順とし、右クリック・キャプチャ喪失・現ホーム行への再設定は手順外。機械 probe
  (CpUiG6PlacementTests/DndMoveTests)も VM コマンド直接実行が主で入力イベント層のボタン種別を
  検査していない。
- 未確定事項との関係: なし(TAG-014/015 は消化済み。本件は裁定不要の堅牢性欠陥)。

## 3. 切り分け済みの事実(2026-07-17 コード実測)

- `OnPaletteItemMoved:109`/`OnNodeRowMoved:307` は press 状態フィールドのみで判定し、
  `e.GetCurrentPoint(...).Properties.IsLeftButtonPressed` を見ない。`PointerCaptureLost` ハンドラは
  ファイル内に存在しない(grep 0 件)。
- 挿入系 4 ハンドラに `IsLeftButtonPressed` なし(grep: 該当ファイルの左ボタン確認は :90/:101/:292 の 3 箇所のみ)。
- `SetHomeFromMenu:572` は `node.IsHome` を確認せず全行クリア→設定→`SetDirty(true)`。
- **未検証(疑い・fix 時に確定)**: headless での症状 1 の決定論再現性(キャプチャ喪失はプラットフォーム
  イベント。GF-092-02 前例=headless が再現しない欠陥は VM/合成入力 probe で内部契約を pin する)。

## 4. 是正方針(案・着手時確定)

1. **症状 1**: `OnPaletteItemMoved`/`OnNodeRowMoved` の冒頭で実測ボタン状態
   (`e.GetCurrentPoint(this).Properties.IsLeftButtonPressed`)を確認し、非押下なら press 状態をクリアして
   return(状態残留の自己回復)。加えて行/カードに `PointerCaptureLost` ハンドラを配線し press 状態をクリア。
2. **症状 2**: 挿入系 4 ハンドラへ `IsLeftButtonPressed` ガード(既存 3 箇所と同型)。
3. **症状 3**: `SetHomeFromMenu` で `node.IsHome` が既に true なら no-op(単一ホーム不変が前提のため
   全行クリアも不要)。
4. **プローブ(R5)**: (2)= headless で挿入ポイント座標へ右ボタン `MouseDown` → ツリー不変(是正前=挿入されて赤)。
   (3)= VM probe: 現ホーム行へ SetHomeFromMenu → IsDirty 不変(是正前赤)。
   (1)= headless `MouseDown(左)`→`MouseMove(RawInputModifiers.None・閾値超え)` でドラッグが開始しない
   (DraggingNode/PlacingTag が立たない)を試行。合成イベントで決定論化できない場合は GF-092-02 前例に
   従い「headless 非再現」を記録し、ボタン実測ガードの挙動 pin(非押下 Move の no-op)へ切替。
5. **CP 追補**: CP-UI-G6 へ「入力堅牢性」次元(右クリック非破壊・非押下移動の非発火・無変更操作の
   dirty 不変)を潜伏実績つきで追加。

## 5. 影響 BOM

- **src**: `TagsTabView.axaml.cs`(Moved 2 箇所のボタン実測ガード+PointerCaptureLost 配線+挿入系
  4 ハンドラの左ボタンガード)+`HierarchyEditorViewModel.cs`(SetHomeFromMenu の no-op ガード)。
  XAML/style/i18n/CAD/DB= 変更なし見込み(視覚不変)。
- **tests**: 新規 probe(§4-4)。既存 CpUiG6PlacementTests/DndMoveTests 全緑維持。R6= 固定 Oracle 不変。
- **CP**: CP-UI-G6 へ入力堅牢性次元。
- **E/M-BOM**: 変更なし見込み(契約自体は不変・実装品質の是正)。

## 6. 残ゲート

- gate①(裁定): **不要**(CAD/BOM 沈黙の実装欠陥。是正は挙動追加でなくガード)。
- gate②(golden): **必要**(操作系の是正=実機確認)。基準案: 配置モード中の右クリック/中クリックで
  階層不変・現ホーム行へメニューの「ホームに設定」で保存が活性化しない・通常のクリック配置/D&D/
  クリック操作の回帰なし(視覚不変)。

## 7. `/eco-fix` 実施記録(2026-07-17)

### 7.1 プローブ先行(R5)

新規 `CpUiG6InputRobustnessTests`(4 本)。是正前に **4/4 の赤を実測**(2 回の実行に分かれる —
理由は下記):

- ③無変更ホーム再設定= VM 決定論で赤(SetDirty(true) が実測)。
- ②右クリック配置= headless 実マウスイベント(MouseDown Right)でルート末尾ポイントが挿入を実行し赤。
- ①非押下移動(パレット面/行面)= 「press(左)→ボタン情報の無い移動」で誤ドラッグ機構
  (BeginDragPlacing/BeginMove→finally 解除)が**既存のクリック配置モードを破壊**する持続観測で赤。

**計装知見(記録)**: 是正前状態ではプローブ同士が干渉し 4 本同時には赤にならない —
(a) headless セッション(PerAssembly 共有=M-HARNESS-015)は**ポインタ押下状態をテスト間で共有**し、
MouseUp しないテストの後続で MouseDown が無効化される(診断= InputHitTest は正・press 未記録で特定)。
(b) 是正前は誤経路が `DoDragDropAsync` へ到達し in-flight のドラッグループが後続テストへ波及。
対処= 全プローブで Down/Up を対にする+冒頭に掃除の MouseUp(是正後は誤経路が
DoDragDropAsync へ到達しないため決定論)。**マウスイベントを使う headless テストは Down/Up を
必ず対にする**を CP-UI-G6 へ注意事項として刻印。

### 7.2 是正内容(最小構成・§4 案のとおり)

- **①**: `OnPaletteItemMoved`/`OnNodeRowMoved` 冒頭で実測ボタン状態を確認し、非押下なら press 状態を
  クリアして return(残留の自己回復)。行/カードへ `PointerCaptureLost` を配線し press 状態をクリア。
- **②**: 挿入ポイント 3 種+「＋ 子にする」の 4 ハンドラへ `IsLeftButtonPressed` ガード
  (既存 3 箇所と同型)。
- **③**: `SetHomeFromMenu` は `node.IsHome` なら no-op(実変更なし=ダーティ・modified_at 不変)。
- **横断規約(ECO-080)**: 新文言なし・XAML 変更はイベント属性の追加のみ・視覚不変。

### 7.3 機械受入

build 0 error / **Tests 778/778**(プローブ 4 本緑転+クリック経路 pin 2 本込み・既存 774 不変)/
Oracle 109+2skip(R6 不変)/ validate_bom 0/0。**R7= 対象外**(視覚差分なし=ガード追加のみ・
ECO-098 前例)。M4= CP-UI-G6 へ入力堅牢性次元を刻印(E/M-BOM は契約不変のため変更なし)。

## 8. 残ゲート(更新)

- gate②(golden)のみ。合格基準は §6 のとおり(右/中クリックで階層不変・現ホーム行への再設定で
  保存非活性・クリック配置/D&D/クリック操作の回帰なし=視覚不変)。
