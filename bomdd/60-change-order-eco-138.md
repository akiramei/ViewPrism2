# ECO-138 — 二段階スキャンのステージングが変更全件をメモリ保持(26 万件級全面差分のピークメモリ未封止)

- 種別: 不具合候補(実装/設計層・性能特性・**要実測**。gate① 要否は工程診断で確定)
- status: staged(2026-07-23 起票。出典= ECO-134 R3 スコープ外所見2+所見4 残り・maintainer 裁定で単独起票)
- baseline: main `f217470`
- 優先度: 中(着手順序= ECO-137 クローズ後・実測先行)

## §1 症状

二段階スキャンのステージングが変更全件をメモリ保持する:

- [ScanService.cs:166-169](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L166) の
  statusUpdates/deletes/adds/metaUpdates リスト+
  [:186](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L186) の判定用ビュー `current`
  (existing 全行の ImageRecord)
- [ScanStaging.cs:83](../src/ViewPrism2.Core/Models/ScanStaging.cs#L83) の `Adds`= 完全 ImageRecord 群
  として、サマリー提示〜適用まで保持

26 万件級の全面差分(wrong-drive・大量移動)でピークメモリ大。**RSS 上限・計測ガードなし**。
既往同族= ECO-062(6,698MiB 消費・26 万件全走査)— メモリ×スケールの検査盲点クラスの 2 例目。

## §2 工程診断(**本 ECO の第 1 作業 — 未確定**)

staging 全件保持が二段階スキャンの意味論とどこまで結合しているかを確定してから案を出す:

1. **適用のアトミック性/部分失敗回復** — ECO-133 の回復モデル(部分適用あり得る・回復は再スキャン)
   はバッチ適用前提。ストリーミング化はコミット単位を変えうる。
2. **サマリー集計** — ECO-136 の TotalMissingAfterApply は staging 由来。集計確定時点が変わる案は
   率カードの意味論に触れる。
3. **キャンセル・進捗** — Phase 境界(Stage 完了→Summary→Apply)の観測点が変わるか。

結合が深い案(ストリーミング/バッチ化)は**意味論変更= gate① 裁定必要**。
軽量化のみ(下記案a)なら実装層で閉じる見込み。

## §3 切り分け済みの事実

### 確定(コード実測)

- 保持箇所は §1 のとおり(現 HEAD で確認済み)。ImageRecord 完全体を Add 対象全件で保持。

### 未検証(疑い — /eco-fix の最初に実測)

- 26 万件級全面差分での実ピーク(allocation/RSS)。合成 staging(26 万件相当)の allocation 計測
  (I/O・SQLite 除外)で下限を得る。**実測が許容内なら是正せず封止のみで閉じる**
  (必要性を実測してから — 案c)。

## §4 是正方針(未定 — 実測+工程診断後に確定。方向の候補のみ記録)

- **案a**: staging 行の軽量化(完全 ImageRecord でなく適用に必要なフィールドのみ)。意味論不変・効果中。
- **案b**: バッチ/ストリーミング適用。効果大だが §2 の結合次第で意味論変更= gate① 必要。
- **案c**: 是正せず封止のみ(計測+上限ガード+警告)。実測が許容内ならこれで閉じる。
- **受入形式(R5 で設計)**: 走査回数型の決定論プローブは使えない — **件数比例の allocation 上限
  プローブ**(GC.GetAllocatedBytesForCurrentThread 等)で「staging メモリは件数 N に対し
  O(N×行サイズ上限)」を pin する形を設計(固定時間閾値を置かない方針のメモリ版)。
- **検査封止(所見4 の残りを本 ECO で併修)**: スキャン CP へ **ピークメモリ特性+区間別 throughput**
  を追加(ECO-134= 計算量・ECO-137= 走査回数に続く残り 2 軸 — 性能 CP の多軸封止)。
- **停止意味論の 2 不変条件(起票時から R5 対象)**: staging を触るため token 伝播・世代ガードの
  CP 行チェックを含める(ECO-075 昇格規則・control-plan)。

## §5 影響 BOM

- `src=ScanService.cs / ScanStaging.cs`(方針確定後に確定)
- `tests=` allocation プローブ(+案b なら適用意味論の回帰)
- `bomdd/33-control-plan.yaml`= スキャン CP へピークメモリ+区間別 throughput 行
- `CAD`= なし見込み(進捗・サマリー表示が変わる案を採る場合のみ申し送り)

## §6 残ゲート

- **gate①(裁定)= 工程診断後に判定**(案b= 必要・案a/c= 不要見込み)。
- **gate②(golden)= 案に依存**(案a/c= 不要見込み・案b= サマリー/進捗の実機確認)。
- 着手条件: ECO-137 クローズ後+§3 実測先行。
- 関連: ECO-134(R3 所見元)・**ECO-062(既往同族= メモリ×スケール)**・ECO-129/130(二段階スキャン)・
  ECO-133(部分失敗回復)・ECO-136(サマリー集計が staging 由来)。
