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

## §4 是正方針(**gate① 裁定確定 2026-07-23= 案A・初版 new+candidate 限定**)

- **案A(hash 厳格)= 採用**: 「高信頼=hash 一致」に限定。**初版の自動裁定対象は new+candidate(青)のみ**
  = 同一フォルダに同ハッシュ missing がある再スキャン新規で、hash 一致が既に `candidate_link_id` として
  在庫にある(§3)。**新規配線ゼロ・誤裁定リスク最小**で価値を出す。
  - reappeared(灰)の hash 再計算+旧 hash 比較配線は**次版(案A 拡張)へ分離**(初版スコープ外)。
  - 自動裁定が行う遷移=「**受け入れる(→normal)**」相当をバッチ適用。
- **案B(緩め)= 不採用**: size+mtime 一致のみでの同一みなしは誤受入リスク(別画像が同サイズで置かれる)。
  「乱暴でない」要件に反する。
- 共通: 自動裁定は「**対象バッチ選別 → 件数/信頼度を提示 → 確認(CMP-011 準拠)→ 一括適用(可逆)**」。
  適用の原子性/可逆性は既存 T13(受け入れる=pending→normal)の可逆経路に合わせる。

## §4.1 CAD モック作成指示(デザイナー向けブリーフ・ViewPrismUI 先行)

[pending_review.md](../../ViewPrismUI/docs/screens/pending_review.md) を次で改訂し、mock/captures を追加する
(**CAD が正=製品コードより先**):

1. **PEND-003 の再裁定**: 「一括なし」→「**高信頼サブセットの一括自動裁定を許可**(初版= new+candidate の
   hash 一致のみ)。個別裁定+保留は従来どおり残す」。裁定履歴に PEND-003(再)として追記。
2. **自動裁定ボタン**: 高信頼候補が 1 件以上あるとき活性。ラベルに**対象件数**を出す(例「ハッシュ一致 N 件を
   自動裁定」)。0 件なら不活性/非表示。
3. **信頼度の提示**: 対象が「なぜ同一と断定できるか」を明示(**ハッシュ一致**=反証不能)。灰(reappeared)・
   琥珀(changed)は**対象外である旨**も面で明示(誤解防止)。
4. **確認ダイアログ(CMP-011 準拠)**: 適用前に件数・遷移(→受け入れ=normal)・**可逆性**を提示して確認。
5. **適用後**: バッチ分を一覧から除去し、残(灰/琥珀/candidate なし新規)は個別裁定へ。可逆(受け入れの
   既存取り消し経路)。
6. **スコープ表記**: 初版は new+candidate 限定・reappeared の hash 比較は次版、と面/裁定履歴に残す。

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

- **gate①(CAD 裁定)= 裁定済み(2026-07-23)**: 案A(hash 厳格)・初版 new+candidate 限定・一括自動裁定を許可
  (PEND-003 再裁定)。残= ViewPrismUI での**モック改訂の実施**(§4.1 ブリーフ)= CAD 是正が先。
- **gate②(golden)= 要**: UI 新設のため maintainer 実機 golden(バッチ提示・可逆性の実機確認)。
- 着手条件: **ViewPrismUI モック改訂+承認 → その後 /eco-fix**(製品コードは CAD 反映後)。
- 関連: ECO-129/REQ-101(pending 意味論)・PEND-001〜004(CAD 裁定履歴)・[[eco128-image-state-model-overhaul]]
  (二段階スキャン・pending)・CMP-011(確認ダイアログ)。

## §7 BDR(bdr-01 予測凍結 — 2026-07-23・/eco-fix 前)

BomDD 実験 bdr-01(EXP-20260723-01)のトリガー ECO。**迷う境界=高信頼バッチ自動裁定+選別ロジックの
部品境界**(対象条件②分割案と結合案の双方に合理性+④見直し可能性の認識)。凍結証明= BomDD
`loops/bdr-01/protocol.md` 凍結コミット。**本予測は /eco-fix の影響分析・実装より前に固定**したもので、
実績(実 diff)を見た後の書き換えは行わない(protocol §事前凍結=予測として採点)。

```yaml
boundary_hypothesis:   # 対象条件②/④ — 高信頼バッチ自動裁定の部品境界(記録先=案a: PendingReview surface owner 品目/転記先 30-ebom は /eco-fix で成立時)
  premise: >
    初版(new+candidate 限定)の高信頼バッチ自動裁定は、独立の「同一性判定」ドメイン部品ではなく、
    PendingReview クラスタ(PendingReviewService+VM)内の受入(T13: pending→normal)の一括適用+
    既存 candidate_link_id への単純フィルタとして宿る。高信頼シグナル(hash 一致)は既に scan が
    candidate_link_id として在庫化済みで、新たな判定ロジックも scan との結合も要さない。よって
    結合案(PendingReviewService 拡張)で受け止め、新規ドメイン部品は作らない。
  expected_change: >
    new+candidate 限定の要求変更・是正は PendingReview クラスタ内の diff で完結する想定
    (ScanJudge/ScanStaging へ触れない)。切り出す場合の実体は所有の移転でなく、選別フィルタ+
    一括受入 API の追加=unit 粒度規準に照らし複合 unit 化しない。
  review_trigger: >
    PEND-005(reappeared の自動裁定)が起票され、旧 hash 再計算+比較を ScanJudge/ScanStaging へ
    供給する配線が必要になった時=「同一性の確信度」が scan と review を横断する第2消費者を得るため、
    独立部品への切り出しを再裁定する。または高信頼選別が単純フィルタを超える(閾値設定・複数基準の
    合成・第3の消費者出現)時。
```

**/eco-fix 時の 4 値照合予告**: 実 diff が PendingReview クラスタ内で完結し ScanJudge/ScanStaging へ
触れなければ「想定範囲内」。scan 側に配線が漏れれば「前提疑義」(premise の結合不要仮説の反証)。

## §8 実施記録(/eco-fix・2026-07-23)

### 製造
外部 AI 工場 **Codex**(/factory-delegate 3 例目)へ狭義製造を委譲。設計者(Claude)保持=境界決定・
boundary_hypothesis 転記(実装 diff 前 commit `2cdf182`)・4 値照合・R8 独立受入検査。実 diff=17 ファイル
(Core: PendingReviewService `IsHighConfidence`+`AcceptHighConfidenceAsync`/IImageRepository+ImageRepository
`AdjudicatePendingBatchAsync`〔単一トランザクション・pending WHERE 強制・affected≠要求で全 rollback〕・
GetByIdsAsync〔chunk IN・N+1 回避〕/App: PendingReviewViewModel〔グループ化・AutoAdjudicate・件数パリティ〕・
PendingReviewWindow・ConfirmDialog〔PD-6 一覧〕・WindowService/i18n ja+en 13 キー/spec §2.11.7・
30-ebom E-UI-PENDING-049 invariant〔PEND-003 例外〕・control-plan)。

### bdr-01 4 値照合 =**「想定範囲内」**(予測=結合案が的中)
実 diff は PendingReview クラスタ内で完結し **ScanJudge/ScanStaging・scan 経路に無接触**(git status で確認・
Codex も明示報告・上流不変条件は ScanJudge.cs:104-109=candidate は hash 一致 missing にのみ付与、で裏取り)。
新規「同一性判定」ドメイン部品は作られず、PendingReviewService 拡張+既存 candidate_link_id 流用で受けた。
前提疑義(scan 結合の強制)・強度超過(想定外の変化軸)は発生せず。review_trigger(PEND-005 reappeared)は
未到来=該当なし。boundary_hypothesis は無変更(採点の後付け防止)。

### 受入(機械・設計者再実測=工場自己申告の裏取り)
build 0/0・`dotnet test tests/ViewPrism2.Tests` **948/948**(+11・追加プローブ緑)・Oracle 109 pass/4 skip
(**無接触=R6 OK**)・validate_bom 0/0。

### R7 セルフゴールデン
Gf 視覚パリティ(GfPendingReviewVisualParityTests=PD-5 callout+グループ+自動チップ・
GfConfirmDialogVisualParityTests=PD-6 対象一覧つき確認)が CAD captures 契約に対して緑=転写漏れ 0。

### R8 セルフレビュー(fresh context 独立受入検査)
中核ロジック(高信頼選別=new+candidate 厳密・原子バッチ全 rollback・件数パリティ単一母集合・可逆性・
プローブ非空虚)は正と確認。**スコープ内の要是正欠陥 0**。**F-1(共有 ConfirmDialog の基底 Margin/Spacing/
FontWeight 無条件変更が既存確認面〔削除/再リンク/未保存終了〕へ視覚波及)= 案超過**を maintainer 裁定
(案①)で**PD-6 版へスコープ限定是正**(既定は元の 16/16/通常太さへ戻し・PD-6 のみ code-behind で上書き=
既存 golden 不変)。F-2(pending 一覧の非仮想化=既存構造・別 ECO 候補)・F-3〜5(様式)はスコープ外/軽微。

## §9 残ゲート(実装後)
- **gate②(実機 golden)= 要**: maintainer 実機で下記を確認(操作手順は fix 報告に提示)。
- bdr-01: ECO クローズ後に protocol §42 測定 3 点(BDR 作成/照合/保守コスト・寄与・形骸化)を BomDD へ記録。

## §10 GF-139-01 — 自動裁定の動作を relink へ再裁定(gate② golden レビュー由来・2026-07-23)

**所見**(maintainer 実機レビュー): 自動裁定=「受け入れる(pending→normal)」は、移動/リネームで生じた
**一致 missing(リンク切れ)を残したまま**にし、元画像のタグも孤立させる。高信頼=同一画像の移動なのだから
不自然。**実装は承認モックに忠実(accept)だが、承認した動作そのものを見直す**(gate① 再裁定)。

**再裁定(2026-07-23・案A→動作変更)**: 自動裁定 = **候補 missing への一括 relink**(`ApplyRelinkAsync`/T4・
REQ-017)。元画像の image_id/タグを保持し新パスへ付け替え、pending 行削除、**missing も解消**。
- **曖昧ケースの扱い**: 同一 missing に複数の新規が一致する場合は自動対象から除外し手動へ(自動は 1 missing:1 新規の
  明確な移動のみ)。高信頼件数 N= 一意に relink 可能な組数。
- **CAD 影響**: PD-5 callout ボタン「(N 件を再リンク)」・PD-6 確認「元の画像に付け替え(タグ・ID 保持・リンク切れ解消)」+
  対象一覧の一致先=「<相手> へ再リンク」。ViewPrismUI 再改訂+captures 再生成+**再 golden**。
- **実装影響**: `AcceptHighConfidenceAsync`→ relink バッチ(候補ごとに ApplyRelinkAsync・原子・曖昧除外)。プローブは
  「missing 解消+image_id/タグ保持+pending 削除」を pin。
- **bdr-01**: relink も既存 candidate_link_id 使用・scan 非接触=**境界「想定範囲内」維持**(boundary_hypothesis 無変更)。
- 手順: CAD 再改訂(/cad-mock)→ 再 golden(設計)→ 再実装(/eco-fix)→ 再 gate②。
