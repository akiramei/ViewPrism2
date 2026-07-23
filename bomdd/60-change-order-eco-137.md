# ECO-137 — 再スキャンが DB 行ごと File.Exists+ディレクトリ全列挙の二重メタデータ走査

- 種別: 不具合候補(実装層・性能特性・**要実測**。挙動は仕様どおり)
- status: staged(2026-07-23 起票。出典= ECO-134 R3 スコープ外所見3・maintainer 裁定で単独起票)
- baseline: main `f217470`
- 優先度: 中(着手順序= ECO-138 より先。ECO-134 と同型の軽量クローズ見込み)

## §1 症状

再スキャン(StageCore)が同一フォルダ木に対しメタデータ走査を二重に行う:

1. [ScanService.cs:162](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L162) で
   `CreateEnumerable(folder, root)`(ディレクトリ全列挙)を用意
2. [ScanService.cs:197](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L197) で
   **existing 全行に対し 1 行ずつ `File.Exists`**(missing 判定)
3. [ScanService.cs:234](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L234) で列挙を walk

HDD では 2. の**行ごとランダム存在確認**が支配的になり得る(管理 26 万件級)。
ScanCore(初回経路)にも同型([:497](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L497)/
[:514](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L514))があるが、初回は existing≈0 で
実害小 — 主対象は再スキャン経路。

## §2 工程診断(起票時見込み)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD | 健全 | 視覚・挙動変更なし |
| BOM(検査封止) | 部分欠落 | 存在確認の I/O 走査回数は CP 未封止(ECO-134 §4 計算量封止と同族の残り) |
| 実装 | 逸脱疑い | 結果は正・メタデータ走査の形のみ(列挙で得られる情報を行ごと再取得) |

## §3 切り分け済みの事実

### 確定(コード実測)

1. 走査 2 系統の位置は §1 のとおり(現 HEAD で確認済み)。
2. パス照合の比較子は `byPath` 構築(:223 近傍)で `StringComparer.OrdinalIgnoreCase` — 列挙由来の
   相対パス集合を同じ比較子で構築すれば既存の照合意味論と揃う。
3. ECO-134 教訓の read-across: 本件は「毎回走査」型のインデックス不在の一形態(File.Exists N 回
   → 列挙 1 回からの set 構築)。

### 未検証(疑い — /eco-fix の最初に実測)

- HDD 実測での 2. の支配性(合成計測は下限。mass 管理件数でのランダム存在確認 vs 全列挙の実測比較)。
- `File.Exists` と列挙の意味論差: 「列挙に現れないが File.Exists は真」のクラス(アクセス権・
  共有違反・隠し属性等)の存在と扱い。観測時点の差(TOCTOU)は現行も列挙(:162)と行ごと確認(:197)で
  二時点あり、統合はむしろ一時点化 — ただし R5 で挙動同値を pin してから是正する。

## §4 是正方針(案・着手時確定)

- **案A(推奨)**: 列挙 1 回から相対パス集合(`OrdinalIgnoreCase`)を構築し、行ごとの存在確認を
  set lookup 化 = メタデータ走査をディレクトリ列挙 1 回に統合。ECO-134 §7 と同型
  (一度構築した写像への O(1) 照合)。
- **プローブ先行(必須・ECO-134 の型)**:
  1. **計数プローブ**: 存在確認のファイルシステム呼び出し回数を数える抽象化で
     「行ごと File.Exists 呼び出し == 0(存在確認は列挙 1 回に集約)」を pin。
     決定論・固定時間閾値なし(走査回数 40000→200 反転と同じ受入形式)。
  2. **挙動同値プローブ**: missing 判定の結果不変(存在/不在・大文字小文字差・列挙外ファイルの
     各ケースで是正前後一致)。
- **検査封止(併修)**: スキャン CP へ「存在確認の走査回数(列挙 1 回)」観点を追加
  (ECO-134= 計算量に続く走査回数軸。ピークメモリ・区間別 throughput は ECO-138 で併修)。
- **停止意味論(起票時チェック済み)**: 既存 `ct.ThrowIfCancellationRequested` の粒度は不変とし、
  走査統合で cancel 到達性が変わらないことを R5 に含める(control-plan「停止意味論の 2 不変条件」)。

## §5 影響 BOM

- `src=ScanService.cs`(StageCore の存在確認を列挙集合 lookup へ。ScanCore 同型は実測で要否判断)
- `tests=` 計数プローブ+挙動同値プローブ
- `bomdd/33-control-plan.yaml`= スキャン CP へ存在確認走査回数の観点
- `CAD`= 該当なし(視覚・挙動変更なし)

## §6 残ゲート

- **gate①(裁定)= 不要見込み**(実装層・意味論変更なし)。ただし §3 未検証の
  「列挙に現れない存在」クラスで挙動差が出る場合は軽微裁定へ切替。
- **gate②(golden)= 不要見込み**(挙動不変+視覚変更なし= ECO-134 と同じく機械受入が納品条件の想定)。
- 着手条件: なし(プローブ先行のみ)。関連= ECO-134(同型・R3 所見元)・ECO-130(二段階スキャン)。
