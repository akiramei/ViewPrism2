# Migration Oracle — <ECO/CAPA-ID> データ移行専用オラクル(Phase 7)

> **[disposition: 未採用テンプレート / unused-template]**
> 本ファイルは BomDD テンプレ一式の移行オラクルテンプレ。**本プロジェクトでは出番がない。**
> 理由: ECO-001〜015 を通じて**スキーマ変更を伴う ECO が皆無**(全期間で横断固定オラクル S-01〜S-31 を凍結 `loop-v4-r1`・migration 追加ゼロ)。移行オラクルはスキーマ変更 ECO が出た時点で起こす。
> ECO 索引は [60-change-register.yaml](60-change-register.yaml)、正本宣言は [00-manifest.yaml](00-manifest.yaml) を参照。
> 下記テンプレ本体は参照用に保持する(将来のスキーマ変更 ECO 用の雛形)。

> データは**コードと違い再鋳造で交換できない**(永続状態が残る。gap-analysis §A3)。スキーマ変更を伴う ECO では、回帰(既存固定オラクル)と別に**移行専用オラクル**を立てる。
> 工場非開示: **オラクル実装と fixture の期待値(manifest)は工場へ渡さない**。移行要件(REQ)としての仕様は渡す。

## 1. fixture(As-Maintained 個体の実データ)
- 入力 DB: <変更前個体(60 §0 で凍結した baseline)のビルドで作成した実データ。例: `oracle/fixtures/baseline-vNN.db`>
- manifest: <期待値(件数・状態・並び・金額等)を JSON で固定。例: `baseline-vNN-manifest.json`>
- 実行規律: fixture は温存し、**一時コピーに対して**新ビルドを起動する(移行は不可逆でよい=コピーが移行される)

## 2. 検査行(M 行)と失敗分類
| ID | 検査 | 失敗時の分類 |
|---|---|---|
| M01 | 旧スキーマ DB で新ビルドが起動する(移行実行・エラー/データ消失なし) | data-preservation miss |
| M02 | 既存データ(一覧・件数・状態・並び)が API から manifest と同値で取得できる | data-preservation miss |
| M03 | 移行後の個体に**変更後の新ルールが適用される** | change miss(移行後規則の適用漏れ) |
| M04 | 履歴・派生値(料金等)の保持 | data-preservation miss |

## 3. 較正(negative control — 凍結前必須。playbook §4.4)
- 移行オラクルを**変更前個体**に実行し、事前宣言した期待プロファイルと突合する
- 固定オラクル(新規行込み)も変更前個体に実行: **既存行=PASS・新規行=FAIL** を確認(forward-01.5 では較正の初回実行が M03 の判定順序干渉 CHEAT-F015-H001 を凍結前に捕捉)
- 較正結果は記録として残す(`calibration-*.json`)

## 4. 結果(製造後に記入)
| 工場 | M 行 | 帰属 |
|---|---|---|
| | | |
