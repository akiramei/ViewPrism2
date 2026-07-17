# Change Order — ECO-104(staged): 保存バー表示状態の残欠陥 — トーストの dirty/ビュー切替への残留+保存失敗文言の言語非追随(ECO-103 レビュー所見 P2×2)

- 起票: 2026-07-17(maintainer レビュー所見・ECO-103 applied 後の P2×2)
- 種別: 不具合是正(ECO-103 混入・実装層)
- baseline: main `ad51f92`

## 1. 症状(レビュー所見・2026-07-17)

機械受入は全緑(Tests 790/790・Oracle 109+2skip・validate 0/0)= いずれも既存プローブの未検査ケース。

### 1.1 保存トーストが次の編集状態へ残留する

保存成功後、トースト(「✓ 変更を保存しました」・1.8s 自動消滅)の解除経路が**タイマのみ**。

- 1.8s 以内に再編集すると、**成功トーストと未保存バーが同時表示**される(「保存しました」と
  「未保存の変更があります」の矛盾掲示)。
- 保存直後(クリーン)に別ビューへ切り替えると、**新しいビューの上に前ビューの成功トーストが残留**する
  (確認表示が誤った対象へ帰属)。

### 1.2 保存失敗文言が言語変更へ追随しない

`SaveError` に `ErrorMessages.Resolve` **済みの表示文字列**を保存しているため、失敗表示中に設定で
言語を変更しても旧言語のまま。`CultureChanged` は `SaveBarMessage` を再通知するが、保存値自体は
再解決されない。

## 2. 工程診断 — 実装層(ECO-103 混入)。gate① 不要

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 健全(1.1 は沈黙・1.2 は対象外) | 1.1: mock v4 の savedToast は timer のみで解除(プロトタイプ簡略)= dirty/ビュー切替との交差は**沈黙**。ただし VC-TAG-16①「dirty 中**のみ** 3 表示」+④の趣旨(保存の一時確認)から、dirty との同時掲示・別ビューへの持ち越しが意図でないことは自明 — 状態衛生はアプリケーションスコープの関心(ECO-080「モックに書いていない≠規約がない」)。1.2: i18n はモックの管轄外(K-AVALONIA 横断規約の領分)。 |
| BOM | 健全 | CP-UI-G6 に VC-TAG-16 次元は宣言済み。プローブの検査ケース漏れは CP の欠陥ではなく fix 時にプローブ追加で埋める。 |
| 実装 | **欠陥(変更対象)** | §3。両件とも混入= `7370b7d`(ECO-103 fix)。潜伏 0 日(同日レビューで検出)。 |

- 1.2 は**横断規約への逸脱**: 表示文言は表示時に現在ロケールから解決する(ECO-079/080 の 3 層運用+
  ECO-095「デフォルト WS 名の表示時解決」= i18n 立地第 3 様態と同族)。解決済み文字列の保持は
  ECO-095 教訓「値の権威主体と再導出可能性」が既に禁じたアンチパターンの VM 一時状態版。

## 3. 切り分け済みの事実(2026-07-17 コード読解で確定)

- `ResetSaveBarState()`(HierarchyEditorViewModel:479-484)= `_attentionCts` キャンセル+
  `IsGuardAttention=false`+`SaveError=null` のみ。**`_toastCts`/`IsSavedToastVisible` に触れない**。
  呼び出し元= LoadAsync(:759)・SaveAsync 成功時(:1019)。
- `SetDirty(true)` の全経路(:579/:719/:832/:855/:878/:896/:979/:992)も トーストを解除しない。
  → 1.8s 内の再編集・ビュー切替(LoadAsync)でトースト残留(症状 1.1 の機構確定)。
- `SaveBarMessage`(:406)= `SaveError ?? _localization.T(...)`。`CultureChanged`(:289-297)は
  `OnPropertyChanged(nameof(SaveBarMessage))` を発火するが、getter が返す `SaveError` は
  :1015 で `ErrorMessages.Resolve(_localization, result.Error)` 済みの文字列(症状 1.2 の機構確定)。
- マスキング要因: CpUiG6SaveBarTests は「保存→トースト表示→自動消滅」「失敗→attention 維持」を
  単線で検査 — **保存↔再編集/ビュー切替の交差**と**失敗表示中のロケール切替**が未検査ケース。

## 4. 是正方針(案・着手時確定)

1. **1.1(トースト残留)= 案A(最小・推奨)**: `ResetSaveBarState()` にトースト解除
   (`_toastCts?.Cancel()`+`IsSavedToastVisible=false`)を含め、`SetDirty(true)`(false→true 遷移)
   からも解除を呼ぶ。SaveAsync 成功経路は現行の「Reset→ShowSavedToast」順のため影響なし。
   契約= 「トーストはクリーン状態の一時確認 — dirty 遷移・再読込・ビュー切替で即時消滅」。
   (案B= `IsSavedToastVisible` を「!IsDirty && タイマ内」の算出プロパティ化 — 構造的だが
   通知配線の diff が大きく、案A で契約は同値)
2. **1.2(言語追随)= ErrorCode 保持+表示時解決**: `SaveError`(文字列)を `SaveErrorCode`
   (`ErrorCode?` 等の再導出可能な権威値)へ置換し、`SaveBarMessage` getter で
   `ErrorMessages.Resolve(_localization, code)` を呼ぶ。`IsSaveBarAttention` は code の有無で判定。
   CultureChanged の既存再通知(:297)がそのまま効く。
3. **プローブ(R5・是正前赤)**: CpUiG6SaveBarTests へ追加 —
   (a) 保存→1.8s 内に AddNode → `IsSavedToastVisible=false`(dirty と同時表示しない)
   (b) 保存→LoadAsync(別ビュー) → `IsSavedToastVisible=false`(持ち越さない)
   (c) 保存失敗(参照切れ)→ SetCulture("en") → `SaveBarMessage` が en 文言へ追随。

## 5. 影響 BOM(fix 時 M4 で同期)

- **src**: `HierarchyEditorViewModel` のみ(ResetSaveBarState/SetDirty/SaveError→SaveErrorCode/
  SaveBarMessage)。View(XAML)は不変(バインド先名を変えない場合)。
- **tests**: CpUiG6SaveBarTests へプローブ 3 本(§4-3)。既存固定 Oracle 不変(R6)。
- **E-BOM/M-BOM**: 宣言変更なし見込み(E-UI-NODEGRAPH-025 の保存モデル invariant の精緻化に留まる)。
- **CP**: CP-UI-G6 へ検査ケース(トースト×dirty 交差・失敗文言×ロケール)を accept 時刻印。

## 6. 残ゲート

- gate①: **不要**(実装層確定・CAD 沈黙は §2 で診断済み=モック改版不要)。
- gate②(golden): **必要(軽量)**: 実機で ①保存→即編集でトーストが消えバーのみになる
  ②保存→1.8s 内に別ビュー選択でトーストが新ビューに残らない ③**(2026-07-17 差し替え)**
  dirty 中(保存バー表示中)に設定で言語 ja↔en 切替 → バー文言(「未保存の変更があります」⇔
  "You have unsaved changes")が即追随する。

> **基準③の振替記録(2026-07-17)**: 当初の基準③「保存失敗を作る(パレットで配置済みタグを削除→
> 保存)→失敗文言の言語追随」は**実機到達不能**だった — パレット削除は ECO-046 U-a ガードが
> 確認前に拒否するため、REQ-083 の保存失敗(参照切れ)へ実機操作では到達できない(ガードの
> 多層化が意図どおり経路を閉じている)。**CP-UI-G6 刻印済み教訓「golden 基準は実機到達可能な
> 操作で書く」(ECO-045・S-38 機械ガード振替の前例)の再犯**として記録する。SaveError の
> ロケール追随は機械プローブ(§7.1「保存失敗文言はロケール切替へ追随する」= repo 直接削除で
> 参照切れを作為)が恒久ガード。差し替え後の③は同じ表示時解決機構(SaveBarMessage)の
> 実機到達可能な検査面。なお、当初基準③の実施試行が ECO-106(パレット常駐メッセージの
> 言語非追随・別サーフェス同族欠陥)の発見につながった(怪我の功名として経緯を残す)。

## 7. `/eco-fix` 実施記録(2026-07-17)

### 7.1 プローブ先行(R5)

CpUiG6SaveBarTests へ 2 本(3 検査点)追加し、是正前赤を実測:

- 「保存後の再編集とビュー切替でトーストは即時消える」(タイマ凍結=SavedToastDuration 1h で
  状態遷移側の解除責務だけを検査)→ **赤**(再編集後も IsSavedToastVisible=true)。
- 「保存失敗文言はロケール切替へ追随する」(参照切れ= error.notFound・ja↔en 往復)→ **赤**
  (SetLocale("en") 後も ja 文言のまま)。

既存 790 本は緑を起点固定。

### 7.2 是正内容(§4 案A+ErrorCode 保持を採択 — 真因構造の除去)

- **1.1**: `ResetSaveBarState()` へトースト解除(`_toastCts?.Cancel()`+`IsSavedToastVisible=false`)を
  追加(再読込・破棄・ビュー切替で持ち越さない)+`SetDirty(true)` 遷移で同解除
  (「保存しました」×「未保存」の矛盾掲示を状態遷移で構造的に排除)。SaveAsync 成功経路は
  Reset→ShowSavedToast の既存順のため影響なし。
- **1.2**: `SaveError`(Resolve 済み文字列の保持)→ `SaveErrorCode`(ErrorCode?=権威値)へ置換し、
  `SaveError` は**表示時に現在ロケールで解決する算出プロパティ**へ(ECO-095「値の権威主体と
  再導出可能性」準拠)。`IsSaveBarAttention` は code 有無判定へ。CultureChanged で SaveError も再通知。
  公開名 `SaveError`(string?)は維持=既存テスト・意味論とも不変。
- 横断規約適合(ECO-080): 文言は Loc/ErrorMessages 経由の表示時解決に一本化(直書きなし)。

### 7.3 機械受入

build 0 error / **Tests 792/792**(プローブ 2 本緑転・既存 790 不変)/ Oracle 109+2skip(R6 不変)/
validate_bom 0/0。

### 7.4 セルフゴールデン(R7)= 対象外

VM のみの是正(XAML 不変・トースト/バーの視覚様式は不変=状態遷移のタイミングのみ変更)。
静止 capture に差分が出ない種別のため R7 並置は非適用(ECO-038 前例= VM 内部欠陥)。
検証は headless プローブ(7.1)が担う。

### 7.5 M4 要否

E-BOM/M-BOM 宣言変更なし(E-UI-NODEGRAPH-025 の保存モデル invariant の範囲内)。CP-UI-G6 への
検査ケース刻印は accept 時(クローズ 3 点セット)。
