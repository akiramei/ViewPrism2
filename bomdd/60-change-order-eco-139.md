# ECO-139 — 未裁定(PendingReview)の高信頼同一候補を一括自動裁定する機能(PEND-003 再裁定+CAD モック)

- 種別: 機能拡張(UI/UX)。CAD 先行(既存裁定 PEND-003 に反するため gate① 必須)
- status: staged(2026-07-23 起票。報告=maintainer 手動運用)
- baseline: main `4efc32e`
- 優先度: 中〜高(1 万件規模の実運用で裁定操作が線形に膨らむ体験問題)

## §1 症状 / 要求

未裁定裁定画面([PendingReviewWindow](../src/ViewPrism2.App/Views/PendingReviewWindow.axaml) /
[PendingReviewViewModel](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs) /
[PendingReviewService](../src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs))は **1 画像ずつしか裁定できない**
(受け入れる/削除する/別画像として扱う/保留して次へ)。候補が 1 万件あると 1 万回の操作が要る。

- 全件一括裁定は乱暴。だが**同一画像である可能性が極めて高いもの**(ハッシュ一致・拡張子+サイズ一致 等)は
  自動裁定してよいと判断できる。
- 要求: 「高信頼=同一とみなせる候補」を自動判定して**バッチ化**し、「**自動裁定**」ボタンでそれらをまとめて
  裁定する機能。
- UI/UX 変更を伴うため ViewPrismUI(CAD)への**モック作成申し送り**が必要。

> 制約: 本報告に添付されたスクリーンショットは**ゲーム画像(第三者著作物)**を含むため、
> **ソース管理対象にしない**(本 ECO・captures・メモリを含め一切保存しない)。診断は挙動記述のみで接地する。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| **CAD(ViewPrismUI)** | **欠陥=未定義(既存裁定に反する新機能)** | [pending_review.md](../../ViewPrismUI/docs/screens/pending_review.md) は個別裁定のみ規定。**PEND-003「一括受入れ・複数選択裁定は本版なし=個別+保留のみ」(L163)** を明示裁定済み。要求はこれに反するため CAD 再裁定が必須。ただし PEND-002 が「**要望が出たら mock 改版**」と余地を前置き済み(L162)=想定内の拡張入口 |
| **仕様/BOM** | **基準が未定義** | 「高信頼=同一」の判定基準(hash 一致か・size+mtime 一致で足りるか)が spec §2.11.7 に無い。バッチ裁定の意味論(どの遷移へまとめるか・可逆性・原子性)も未規定 |
| **実装** | **健全(逸脱なし)** | 現行は仕様(PEND-003)どおり 1 件ずつ確定。バグではない。**ただし高信頼シグナルの在庫は origin で差がある**(§3)。案によっては新規配線が要る |

**結論**: これは実装バグではなく **CAD 先行の機能拡張**。PEND-003 の再裁定+基準確定+モック改訂(gate①)が先、
製品コードは後(BomDD: CAD が正・mock→UI-IR→BOM→実装)。

## §3 切り分け済みの事実

### 確定(コード実測)

- PendingReviewService / VM は 1 件ずつ裁定(PEND-003 準拠)。src の仕様逸脱なし。
- **高信頼シグナルの在庫は origin で異なる**([ScanJudge.cs](../src/ViewPrism2.Core/Services/ScanJudge.cs)):
  - **new + candidate(青・PendingOrigin.New + CandidateLinkId)**: 再スキャン新規のうち同一フォルダに
    **同ハッシュの missing がある**もの。hash 一致で `candidate_link_id` 付与済み(L107)= **hash 一致
    シグナルが既に在庫にある**。
  - **reappeared(灰・PendingOrigin.Reappeared)**: missing パスにファイルが再出現。size+mtime 一致の
    ケースは `PendInPlace` で **hash を再計算・比較していない**(L76-79・Hash=null)。UI バナー
    「同じ画像が戻ったのか別の画像が置かれたのか判定できません」の技術的理由がこれ。**hash 一致シグナルは不在**。
  - **changed(琥珀・PendingOrigin.Changed)**: size/mtime いずれか差異=内容変更。定義上「同一」ではない。
- CAD は PEND-003 で一括を明示的に見送り済み・PEND-002 で mock 改版の入口を用意済み。

### 未検証(疑い — gate① 裁定/その後の /eco-fix で実測)

- 「拡張子+サイズ一致」だけを同一とみなす**誤自動裁定リスク**(別画像が偶然同サイズで置かれる)。
  hash 一致まで求めるか、size+mtime で妥協するかは**裁定事項**(§4 案A/B)。
- 1 万件規模の自動裁定 UX(バッチ提示・確認・部分適用・**取り消し可逆性**・性能)。基準確定後に設計。

## §4 是正方針(候補 — gate① 裁定+着手時に確定)

- **案A(hash 厳格)**: 「高信頼=hash 一致」に限定。new+candidate は既存 hash 一致を流用・reappeared は
  **hash 再計算+旧 hash 比較を新設**(ScanJudge/staging に old-hash 保持配線)。自動裁定=「受け入れる」相当を
  バッチ適用。**誤裁定リスク最小・配線コスト中**。
- **案B(緩め)**: 「hash 一致 OR (拡張子+size+mtime 一致)」。reappeared の hash 未計算ケースも size+mtime で
  拾う。**取りこぼし少・誤裁定リスク中・配線コスト小**。
- 共通: 自動裁定は「**対象バッチ選別 → 件数/信頼度を提示 → 確認(CMP-011 準拠)→ 一括適用(可逆)**」。
  UI= 自動裁定ボタン+対象件数・信頼度表示。適用の原子性/可逆性は既存 T13/T15 の可逆経路に合わせる。

## §5 影響 BOM(gate① 後に確定)

- **CAD(ViewPrismUI)= 先行必須**: [pending_review.md](../../ViewPrismUI/docs/screens/pending_review.md) 改訂
  (PEND-003 再裁定・自動裁定ボタン・バッチ提示・信頼度表示の面規定)+ mock/captures 追加。
- **仕様**: `bomdd/20-spec.md` §2.11.7 に「高信頼=同一」基準(案A/B の確定)+バッチ裁定の意味論(遷移・原子性・可逆性)。
- **REQ / E-BOM / M-BOM**: PendingReview surface へバッチ裁定 unit+確認ダイアログ(CMP-011)。
- **実装**: `PendingReviewService` にバッチ裁定 API・高信頼判定の算出(案A なら reappeared の hash 比較配線=
  ScanJudge/ScanStaging へ old-hash 供給)。
- **Control Plan**: バッチ裁定の原子性・可逆性・大量件数(1 万規模)の受入観点。
- **制約**: ゲーム画像はソース管理対象外(captures も含め非保存)。

## §6 残ゲート

- **gate①(CAD 裁定)= 必須・先行**: ViewPrismUI で **PEND-003 を再裁定**(一括自動裁定を許すか)+**基準(案A/B)**
  +モック確定。CAD が正=製品コードより先。
- **gate②(golden)= 要**: UI 新設のため maintainer 実機 golden(基準・バッチ提示・可逆性の実機確認)。
- 着手条件: gate① 後に /eco-fix。
- 関連: ECO-129/REQ-101(pending 意味論)・PEND-001〜004(CAD 裁定履歴)・[[eco128-image-state-model-overhaul]]
  (二段階スキャン・pending)・CMP-011(確認ダイアログ)。
