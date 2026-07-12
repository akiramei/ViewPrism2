# ECO-075: 修復画面が大量 missing で事実上ハング(O(M×N) 候補探索+一覧の逐次 Add)

- 起票: 2026-07-12(maintainer 所見・ECO-073 gate② golden 実機確認中に発見)
- 種別: 不具合(性能。既存修復実装の潜在非スケールが ECO-073 の missing 大量登録で顕在化)
- 状態: staged
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
- gate②: golden = maintainer 実機で当該コレクション(26 万 missing)の修復画面が開ける・
  UI が応答すること
