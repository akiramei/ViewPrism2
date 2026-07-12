# ECO-075(applied / クローズ済み 2026-07-13): 修復画面が大量 missing で事実上ハング(O(M×N) 候補探索+一覧の逐次 Add)

- 起票: 2026-07-12(maintainer 所見・ECO-073 gate② golden 実機確認中に発見)
- 種別: 不具合(性能。既存修復実装の潜在非スケールが ECO-073 の missing 大量登録で顕在化)
- 状態: applied(2026-07-13 gate②合格)
- 関連: ECO-005(修復導線)/ ECO-073(missing 参照登録=大量 missing の供給源)/ GF-073-06(同族=重い処理の UI スレッド到達)

## 1. 症状(maintainer 報告・2026-07-12)

- 「画像が 5 つしかないコレクション」で修復を開くと**「スキャン中」のままハング**。UI が固まる。
- 切り分け回答: 直前に取り込み実行あり/**ハング中 CPU は活発**/**アプリ再起動後も単独再現**。
- maintainer の見え方: 「まるで全てのコレクションをスコープにしているよう」。

## 2. 工程診断(R2)

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD | 健全 | 修復画面(原典 RepairModal 準拠・§2.11.5)に候補探索の計算量規定はないが、表示契約は満たせる |
| BOM | 健全 | M-UI-REPAIR-027 / M-RELINK-025 の受入観点は意味論のみ。性能次元は沈黙(下記) |
| 実装 | **欠陥(性能・非スケール)** | 下記 §3 |

**結論: 実装層の性能欠陥。** 対象コレクションには ECO-073 の取り込みで **262,045 件の missing 行**が
登録済み(画像タブは normal のみ表示=INV-010 のため「5 枚しかない」ように見える)。既存修復実装は
missing が少数である前提の潜在的非スケールで、ECO-073 の missing 参照登録が初めて大量 missing を
供給したことで顕在化した。裁定不要(視覚不変・意味論保存の是正)。

## 3. 切り分け済みの事実

確定(コード実測):

1. `RepairViewModel.LoadAsync` → `CountAutoRepairableAsync`(RepairViewModel.cs:157)が
   **missing 1 件ごと**に `RelinkService.GetCandidatesAsync` を呼ぶ。
2. `GetCandidatesAsync` は毎回 `GetByIdAsync`+`GetByFolderAsync`(**フォルダ全行**=当該コレクションは
   約 52 万行)をロードし(RelinkService.cs:44,50)、さらに `CriteriaSearchService.SearchAsync` が
   **もう一度** `GetByFolderAsync` を実行する(CriteriaSearchService.cs:36)。
   = **O(M×N)×2 重ロード ≒ 262,045 × 52 万行 × 2** → 事実上終わらない(CPU 活発)。
3. `LoadAsync` は `MissingImages`(ObservableCollection)へ **26 万件を逐次 Add**
   (CollectionChanged 26 万発・UI スレッド)。Dapper の同期完了と合わせ UI スレッド上で回るため
   **UI がフリーズ**し、事前スキャン(GF-V4-02)完了後もバッジ再描画が起きず「スキャン中のまま」に見える。
4. 修復の事前スキャン自体は選択コレクション限定・バックグラウンド実行(ECO-059)で健全。
   全コレクションを走査する経路は存在しない(「全てを見ているよう」の正体は 2 の反復全行ロード)。

疑い(未検証):

- `AutoRepairAllAsync` / `AutoRepairSingleAsync` も per-missing `GetCandidatesAsync` のため同族の
  非スケール(実行時のみ・件数は自動修復可能数に依存)。本 ECO では Load 経路を先に是正し、
  実測で問題が残る場合に追補する。

## 4. 是正方針(案A を採用予定・着手時確定)

- **案A(採用予定)**:
  1. `RelinkService.CountAutoRepairableAsync(folderId)` を新設 — フォルダ行を**単一ロード**し、
     hash 索引+`CriteriaMatcher`(既存 Core 純粋関数)で per-missing 候補を集合演算。
     判定意味論は `GetCandidatesAsync`+`DeriveAutoCriteria`(hash+拡張子+サイズ・
     exact-hash pending ∪ criteria(Pending∪Normal)・自身除外・**タグ付き除外=INV-015**)と同一。
     タグ有無は memo 化し「ちょうど 1 件」判定に必要な分だけ照会。
  2. `RepairViewModel.LoadAsync` — 行ロード+VM 構築+件数計算を `Task.Run` でバックグラウンド化し、
     `MissingImages` は**インスタンス一括差し替え**(26 万発の CollectionChanged を出さない)。
- 視覚・意味論とも不変(golden への影響なし)。diff=RelinkService+RepairViewModel+probe。

## 5. 影響 BOM

- M-RELINK-025(RelinkService: 集計 API 追加)・M-UI-REPAIR-027(LoadAsync の実行様式)
- CP-UI-G10(fixture へ性能 probe 追加=大量 missing で Load が単一ロード相当で完了)
- 32-mbom 沈黙次元: 「修復画面のスケール前提(missing 大量時の候補探索計算量)」を specified 化

## 6. 残ゲート

- gate①: 不要(実装層確定・視覚不変)
- gate②: golden = §8

## 7. 実施記録(2026-07-12 /eco-fix)

### 7.1 先行probe(R5)

- `CpUiRepairViewModelTests` へ「大量 missing でも LoadAsync が単一ロード相当で完了する」を追加
  (2,000 missing+2,000 pending・一意ハッシュ対・件数正当性+5 秒上限)。
- 是正前実測: **不合格 — LoadAsync 71.2 秒**(2,000 件で。262,045 件では事実上無限= O(M×N) の裏取り)。

### 7.2 是正diff(案A)

- `RelinkService.CountAutoRepairableAsync(folderId)` 新設: フォルダ行を単一ロードし
  hash 索引+`CriteriaMatcher`(既存 Core 純粋関数)で per-missing 候補を集合演算。
  意味論= GetCandidatesAsync+自動修復 criteria と同一(exact-hash pending ∪ criteria(hash+
  拡張子+サイズ・Pending∪Normal)− 自身 − タグ付き(INV-015・memo 化+ちょうど 1 件判定に
  必要な分だけ照会))。
- `RepairViewModel.LoadAsync`: 行ロード+VM 構築+件数計算を `Task.Run` でバックグラウンド化。
  `MissingImages` を ObservableProperty 化し**インスタンス一括差し替え**(26 万発の
  CollectionChanged を出さない)。VM 内の per-missing カウント(旧 CountAutoRepairableAsync)を撤去。
- 視覚・意味論不変。M-RELINK-025(auto_count 契約+スケール不変条件)・32-mbom 沈黙次元
  「修復画面のスケール前提」を specified 化。

### 7.3 機械受入

- `dotnet build`: 0 warning / 0 error。`ViewPrism2.Tests`: **645/645 pass**
  (probe 緑転 — 修復 VM クラス 13 本が 3.2 秒で完走)。
- `ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。`validate_bom`: 0/0。判定は exe 直接実行。
- 疑い(未検証)の残置: `AutoRepairAllAsync`/`AutoRepairSingleAsync` の per-missing 候補探索は
  実行時のみ・自動修復可能件数に依存。golden 実測で問題が残る場合に追補する(§3 記載どおり)。

## 8. gate② golden 操作手順

1. 26 万 missing が登録されたコレクション(先日の取り込み先)で ⋯ から修復を開く →
   **UI が固まらず**、修復画面が開いて missing 一覧と「N 件のリンク切れ画像(M 件が自動修復可能)」
   見出しが表示される(初回表示まで数秒程度・スキャン中バッジが正しく消える)。
2. missing 行を選択 → 候補ペインが応答する(選択単位の候補探索は従来どおり)。
3. 既存の小規模コレクションの修復操作(候補提示・再リンク・除外)に回帰がない。
4. (GF-075-01)26 万 missing のコレクションで missing 行の選択・「自動修復」・
   「すべて自動修復」を実行しても**固まらない**(選択の候補探索・自動修復とも数秒オーダーで応答)。

## 9. golden所見 GF-075-01 の是正(2026-07-12 — 再機械受入)

### 9.1 所見と工程診断

- maintainer 実機(26 万 missing): 修復画面は開けた(§8 項目 1 合格)が、「自動修復」実行で
  **固まった**(強制終了後の確認で**修復自体は成功**していた=commit は走り、その後の処理が固まった)。
- 工程診断: §3 の残置疑いの実測確定。①選択時/自動修復時の `GetCandidatesAsync` が
  フォルダ全行を**2 回**ロード(自身+CriteriaSearchService)し、**UI スレッド上で同期完了**
  (Dapper 同期完了・Task.Run なし)→ 1 操作ごとに長時間フリーズ。②「すべて自動修復」は
  missing ごとに `GetCandidatesAsync` を繰り返す **O(M×N)**(Load 経路と同じ構造の取り残し)。

### 9.2 先行probe(R5)

- `CpUiRepairViewModelTests` へ追加: ①候補探索(RefreshCandidatesAsync)が呼び出しスレッドを
  同期ブロックしない ②2,000 missing+一意ペア 1 組で AutoRepairAllAsync が 5 秒未満+1 件修復。
- 是正前実測: **1 件不合格**(①の同期ブロックで失敗)。

### 9.3 是正diff

- `RelinkService.GetCandidatesAsync`: CriteriaSearchService 経由の**再ロードを撤去**し、
  同じ inFolder 行で `CriteriaMatcher` を直接評価(1 呼び出し=単一ロード・意味論同一)。
- `RelinkService.GetAutoRepairablePairsAsync(folderId)` 新設(単一パスで自動修復ペアを確定。
  `CountAutoRepairableAsync` はその件数へ委譲)。同一候補の取り合いは CommitRelinkAsync の
  検証が拒否(旧逐次探索と同じ帰結)。
- `RepairViewModel`: AutoRepairAll=pairs 方式へ。候補探索(選択時/検索/単一自動修復)を
  `Task.Run` 化+**世代ガード**(非同期化に伴う並行再入で古い探索結果を反映しない)。

### 9.4 再機械受入

- `dotnet build`: 0 warning / 0 error。`ViewPrism2.Tests`: **646/646 pass**(probe 緑転)。
- `ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。`validate_bom`: 0/0。判定は exe 直接実行。

## 10. クローズ(2026-07-13 gate②合格)

### 10.1 実機確認(maintainer)

- §8 の 4 項目すべて合格: 26 万 missing のコレクションで修復画面が開けて UI 応答(missing 一覧+
  自動修復可能数表示)/missing 選択で候補ペイン応答/小規模コレクションの修復操作に回帰なし/
  選択・自動修復・すべて自動修復が固まらない。

### 10.2 再発防止(恒久化の所在)

- CP-UI-G10 characteristic へ「大量 missing でも応答」観点を潜伏実績つきで追記。
- 機械側: `CpUiRepairViewModelTests` の性能 probe(Load=単一ロード相当時間・候補探索の
  非同期ブロック検査・AutoRepairAll 単一パス)。M-RELINK-025 auto_count 契約+32-mbom 沈黙次元
  「修復画面のスケール前提」を specified 化。

### 10.3 教訓

- **新機能が既存部品へ供給する「規模」は影響 BOM の一次元**: 修復画面(ECO-005)は少数 missing
  前提の O(M×N) が導入時から潜伏していたが、当時の入力規模では実害がなく golden も素通しした。
  ECO-073 の missing 参照登録が初めて数十万行を供給して顕在化。機能追加の影響分析では
  「この機能は既存のどの部品に、これまで無かった規模・頻度の入力を与えるか」を問う。
  ECO-062(類似候補の 26 万件全走査)・ECO-026(一覧仮想化)と同族= read-across。
  もう一つ: **性能是正で処理を真に非同期化すると、fire-and-forget 前提だった UI 経路に並行再入が
  生まれる**(GF-075-01 の世代ガード)。同期完了に依存していた暗黙の直列性を、非同期化の際に
  明示のガードへ置き換える(GF-073-06 の Task.Run 化と対)。方法論昇格候補。
