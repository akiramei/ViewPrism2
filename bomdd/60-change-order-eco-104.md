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
  ②保存→1.8s 内に別ビュー選択でトーストが新ビューに残らない ③保存失敗表示中に設定で言語切替
  → バー文言が追随する。
