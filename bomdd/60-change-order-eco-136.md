# ECO-136 — スキャン結果サマリーの missing 率が delta/総管理の基準不一致で過小表示(空フォルダで 100% にならない)

- 種別: 不具合(集計の基準不一致。率の意味論=CAD 曖昧+実装が delta を分子に流用)
- status: staged
- baseline: main `aed9a90`
- 報告者: maintainer(2026-07-22・手動テスト中)
- 優先度: 中(誤警告=深刻度の過小表示)

## §1 症状

管理 61 件のフォルダを空にして再スキャン(走査 0 件)すると、スキャン結果サマリーの missing 率
カードが **「見つからない画像 51/61 件(83.6%)」** と表示される。フォルダは空で全画像が
見つからないのだから **100% であるべき**。未裁定(pending)0 件・ゴミ箱(deleted)0 件。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **曖昧** | [scan_summary.md:127](../../ViewPrismUI/docs/screens/scan_summary.md) は「missing 率 = 見つからない件数 ÷ 管理件数」だが、**「見つからない件数」が delta(今回遷移)か total(適用後総 missing)か未分離**。mock 例示(SC-3=3.8%・SC-4=99%)は全損=delta≈total を暗黙前提で、既存 missing 混在時の意味論を規定していない |
| BOM/spec §2.1(ECO-130) | 未規定 | 率の分子の母集合(delta/total)を明記していない |
| 実装 | **delta を流用** | 率/tier の分子に `MissingTotal`(= delta)を使い、分母は総管理数=基準不一致 |

結論: **率の意味論が未確定(CAD 曖昧)+実装が delta を分子に流用**。警告文言(現在の健全度を述べる)と
ユーザー期待は「適用後の総 missing 数」を指す。是正は率の分子母集合の**確定(裁定)**を伴う → gate①。
遷移サマリー行(見つからない normal→missing 51)は delta 表示で**正しい**(率カードのみの問題)。

## §3 切り分け済みの事実(確定と未検証を分離)

### 確定(コード実測)

1. missing 率 = [ScanSummaryViewModel.cs:352-353](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs#L352)
   の `s.MissingTotal * 100.0 / s.ManagedTotal`。分子=`MissingTotal`・分母=`ManagedTotal`。
2. `MissingTotal` = [ScanStaging.cs:92](../src/ViewPrism2.Core/Models/ScanStaging.cs#L92)
   `MissingFromNormal + MissingFromPending` = **今回新たに missing 化した数(delta)のみ**。既存 missing 行を含まない。
3. missing 判定は [ScanService.cs:196](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L196)
   で **status が Normal または Pending の行だけ**を missing 化。既に missing の行は遷移せず delta に入らない。
4. `ManagedTotal` = `existing.Count`([ScanStaging.cs:38](../src/ViewPrism2.Core/Models/ScanStaging.cs#L38))
   = フォルダの全行(既存 missing も含む)。
5. tier(赤/黄/緑)も同じ `MissingTotal` を使う
   ([ScanSummaryViewModel.cs:347](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs#L347)
   `ScanSummaryLogic.RateTier(s.MissingTotal, s.ManagedTotal)`)= 同様に過小評価。
6. 消去法で残り 10(= 61 − 51)は **「既に missing」の行**(未裁定 0・ゴミ箱 0 = normal でも pending でも
   deleted でもない)。適用後の総 missing = 51 新規 + 10 既存 = 61 = **100%**。
7. 警告文言 `scan.rateDescRed` 等「大部分の画像が見つかりません。保存先のドライブが接続されているか…」は
   **現在の状態**を述べる → 分子は「適用後の総 missing 数」であるべきで delta ではない。
8. 混入 = ECO-130(`7900c8b`・二段階スキャン+率カード導入)以来の as-built。mock 例示が全損
   (delta≈total)前提のため潜伏し、**既存 missing が残った状態で再スキャンした場合のみ**顕在化。

### 未検証(疑い)

- 「残り 10 = 既存 missing」は消去法(未裁定 0・ゴミ箱 0 の実機観測)による強い推定。/eco-fix の
  プローブ(既存 missing 行を仕込んで再スキャン→率が総 missing を反映するか)で最終確定する。

## §4 是正方針(案・着手時確定)

- **案A(推奨)**: 率/tier の分子を **「適用後の総 missing 数」(既存 missing 行数 + delta)**に変更。
  `ScanStaging` に総 missing 数(または既存 missing 数)フィールドを追加し、StageCore/ScanCore で
  `existing` の missing 行数を保持。警告文言(現在の健全度)・ユーザー期待と一致。空フォルダ=100%。
  CAD [scan_summary.md:127](../../ViewPrismUI/docs/screens/scan_summary.md) を「総 missing ÷ 管理」に明確化。
- **案B**: 分母を「スキャン前に present だった数」に揃える(delta/present)。ただし「N 件見つからない」の
  N が delta のままで「空フォルダなのに 61 でなく 51」の直感乖離が残る。
- 遷移サマリー行(51 normal→missing)は delta 表示のまま(正しい)。**率/tier の分子だけ**意味論変更。

## §5 影響 BOM

- `CAD`=scan_summary.md:127 の率定義を「総 missing ÷ 管理」に明確化(ViewPrismUI 申し送り・prose のみ・視覚不変見込み)
- `src`=ScanStaging(総 missing 数フィールド)+ScanService(StageCore/ScanCore で既存 missing 数を保持)+
  ScanSummaryViewModel(率/tier の分子差し替え)
- `tests`=既存 missing 混在での率/tier probe(空フォルダ=100%)+回帰(全損=従来どおり)
- `spec`=率の分子母集合を §2.1 か control-plan に明文化(delta でなく適用後総 missing)

## §6 残ゲート

- ~~gate①(裁定)= 必要~~ → **裁定済み(§7)**。
- **/eco-fix 着手時にプローブで「残り 10 = 既存 missing」を最終確定**。
- **gate②(golden)**: 是正後、既存 missing 混在の再スキャンで率が総 missing を反映(空フォルダ=100%)+
  全損ケースの回帰(従来どおり)。

## §7 裁定(gate①・2026-07-22 maintainer)

- **案A を採択**: missing 率/tier の分子を **「適用後の総 missing 数」(既存 missing 行数 + 今回 delta)**に
  変更する。警告文言(現在の健全度)・ユーザー期待と一致し、空フォルダ=100% になる。
- **CAD prose 明確化を伴う**: [scan_summary.md:127](../../ViewPrismUI/docs/screens/scan_summary.md) の
  「missing 率 = 見つからない件数 ÷ 管理件数」を「**適用後の総 missing ÷ 管理**」に明確化(ViewPrismUI 申し送り・
  prose のみ・視覚不変見込み=mock 再描画不要)。遷移サマリー行(normal→missing の delta 件数)は不変。
- **着手条件**: /eco-fix でまずプローブにて「残り 10 = 既存 missing」を実測確定してから、率/tier の分子を
  総 missing へ差し替える。

## §8 実施記録(fix)

- **プローブ先行(R5)+真因確定**: `CpScanMissingRateTests` 新設(4 本)。主プローブ= VM に既存 missing 混在の
  staging(normal→missing 3=delta 30%=Yellow / 既存 missing 5 → 総 missing 8=80%=Red)を与え、
  `IsRateRed` と率表示を検査。**是正前=`IsRateRed` が False(VM が delta を分子=Yellow)で不合格**を実測
  → 「率の分子=delta」を確定。副プローブ= 実 StageAsync で空フォルダ(既存 missing 2+normal 3・走査 0)を
  ステージし `TotalMissingAfterApply == ManagedTotal`(=100%)を確認(ScanService の PreexistingMissing 採取を裏取り)。
- **是正(案A)**: `ScanStaging` に `PreexistingMissing`(scan 開始時 status=missing 行数)を追加し、
  算出プロパティ `TotalMissingAfterApply = PreexistingMissing − Reappeared + MissingFromNormal + MissingFromPending`
  を新設。`StageCoreAsync` で `existing.Count(Status==Missing)` を採取。`ScanSummaryViewModel.BuildSummary` の
  率/tier/表示件数の 3 箇所を `MissingTotal`(delta)→ `TotalMissingAfterApply`(総 missing)へ差し替え。
  **遷移サマリー行(normal→missing の delta)は不変**。`MissingTotal` は遷移行・TotalChanges・確認ダイアログ等の
  delta 用途で継続使用(R8 で網羅確認)。
- **後方互換**: `PreexistingMissing=0 かつ Reappeared=0` のとき `TotalMissingAfterApply == MissingTotal`。
  既存 golden capture(CaptureHarness 3 サイト=PreexistingMissing=0)・GfScanSummaryVisualParity は不変。
- **diff 規模**: src 3 ファイル(ScanStaging +12・ScanService +1・ScanSummaryViewModel +7/-4)、
  tools/CaptureHarness 3 サイト・tests(CpScanMissingRateTests 新設 4 本+既存 helper 3 サイトに PreexistingMissing=0)。
- **機械受入(4 点・全緑)**: `dotnet build` 0 error ・`ViewPrism2.Tests` **930/930**(プローブ 4 本合格)・
  `ViewPrism2.Oracle` 109 pass/4 skip/0 fail=**凍結オラクル無接触(R6)** ・`validate_bom` 0-0。
- **R7(セルフゴールデン)= 実質対象外**: 率カードの視覚レイアウト・外観は不変(既存 capture は機械検証で不変)。
  変わるのは既存 missing 混在時の表示数値のみで対応 CAD capture は不在(新シナリオ)。CAD prose
  ([scan_summary.md:127](../../ViewPrismUI/docs/screens/scan_summary.md) を「総 missing ÷ 管理」へ明確化)は
  **ViewPrismUI 申し送り**(別リポ・視覚不変見込み)。
- **R8(セルフレビュー)= 実施・所見1(処置済み)**: fix diff を fresh-context の独立レビュアーで実コード検証。
  算術(`Total = P − R + MN + MP` が「適用後 status=missing 行数」と厳密一致・Reappeared⊆PreexistingMissing で
  非負・二重計上/deleted 母集合の整合)・網羅(delta 分子は 3 箇所のみで全置換・残る MissingTotal は delta 用途で正当)・
  後方互換・リグレッション・一段階経路(率カードは二段階のみ=片手落ちなし)を全て正当と確認。
  スコープ内欠陥 0。**指摘 1 件(低・test-coverage: プローブが Reappeared>0 を未行使=`−Reappeared` 項が
  直接未検証)→ 本 ECO 内で是正**(`再出現分は適用後の総missingから差し引かれる` を追加=P5/R2/MN3→total6 を pin)。

## §残ゲート(更新)

- gate①=裁定済み(§7・案A)。真因確定・是正完了。
- **gate②(golden)= 是正後提示(下記)**。合格報告を受けたら /eco-accept eco-136。
