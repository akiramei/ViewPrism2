# Charter — ViewPrism2 Loop V2(ビューア拡張)

<!-- 固定の強さ(phase0-charter.md):
     工場構成・予算・役割・境界種別 = この時点で固定(以後不変)
     題材・スコープ = 仮置き可。`(仮)` を付け、Phase 1 終了時(G1)に確定 -->

- experiment: loop-v2-viewer
- 起案日: 2026-06-13(裁定 4 点はユーザー maintainer が同日確定 — §裁定記録)
- 基準: Loop V1 完了状態(commit 242955c・BOM v1.3・凍結オラクル tag loop-v1-r1・golden G1〜G7 全 approved)

## 題材
- 何を作るか(1〜3行):
  Loop V1 で完成した ViewPrism2 コアに対する増分ループ。原典 view-prism の画像ビューア
  拡張モード(RVP-UI-003)を移植する — scroll / spread-right / spread-left、共通表示設定、
  キーボード操作、設定永続化(移植版設計)。あわせて V1 golden 所見 GF-01〜05(minor 5 件)を是正。
- 種別: GUI(デスクトップアプリケーション)— V1 と同じ
- 黒箱境界:
  - 核(ページ配置計算・ナビゲーション規則・設定モデル): in-process(観測契約が必要)
  - 表面(モード描画・操作・設定 UI): GUI(golden+承認者が必要)

## スコープ
- 含む — Loop V2(G1 確定 2026-06-13 — 要求台帳 REQ-054〜060 へ収束、needs-refinement ゼロ):
  - ScrollMode: 縦連続表示(画面中央の画像を現在位置として追跡)
  - SpreadMode 右開き/左開き: ページ送り 2 ページ/1 ページスライド・空白ページ開始(startWithEmptyPage)
  - 共通表示設定: resizeMode(matchLargerHeight/matchSmallerHeight/noResize)・
    alignMode(top/middle/bottom)・gapMode(tight/loose+customGapPx)
  - モード切替 UI+モードごとの現在位置記憶(切替時に前回位置を復元)
  - キーボード: ←→(spread-right は進行方向反転)・SHIFT+←→(1 ページ送り)・Esc
  - ビューア設定の永続化(移植版設計 — 原典は未実装でセッション限り。V1 の settings 機構
    (表示モード/選択コレクション永続化と同系)へ保存)
  - V1 golden 所見の是正: GF-01(タグ作成ダイアログ高さ固定)・GF-02(グリッド行選択視覚の無効化)・
    GF-03(ホームタグ★→家アイコン)・GF-04(パレット選択タグの視覚強化+追加対象の明示)・
    GF-05(条件サマリの表示用整形 — Unicode エスケープ露出の解消)
- 含まない — 後続ループ / ECO で追加(理由付き):
  - タグ制御モード(2026-06-13 裁定: V3 以降へ defer。原典でもマッピング設定 UI・永続化が
    未実装の未完成機能であり、裁定材料(マッピング UI 設計・保存先)が揃ってから扱う)
  - CustomNormalMode(原典リバースでスタブと確認 — NormalMode と同一実装。移植対象外)
  - ズーム/パン・回転・フィルムストリップ・スライドショー・フルスクリーン(原典未実装の拡張候補
    (viewer-spec §6)。原典互換の対象でない)
  - 類似検索・マージ・高度修復・作業スペース・バックアップ・pending 承認フロー(後続ループ。
    pending 承認は固定オラクル S-01 の凍結意味論との突き合わせ裁定が必要 — crosscheck §7)
  - 動的ソート・view_revisions・サイドバー折りたたみ(ECO-002 crosscheck §8 の明示 defer を維持)

## 工場構成(受入経済性 — playbook §5.2)— V1 踏襲で固定
- ティア: 最小(1工場)— 実用適用。マルチファクトリばらつき測定は行わない
- 使用モデル: Claude(隔離サブエージェント・fresh)。供与物は M-BOM + K-BOM + Routing + Work Order
  +改訂済み仕様/E-BOM のみ。原典 TypeScript ソース・porting-spec・設計対話・固定オラクル(41)・
  探索プローブ(42)は工場非開示
- 収束ループ予算(回数上限): 製造単位あたり 3 回

## 役割
- 設計者(ユーザー+設計AI): maintainer + Claude Code(メインセッション)。
  リバース工程での原典ソースコード読解は設計者側のみ可
- 仕様監査リーダー数(G2): 1(実用適用につき簡略。仕様完了ゲート自体は実施)
- 承認者(知覚・golden がある場合は必須): maintainer(GUI 表面部品の golden 受入)

## 前提・制約
- V1 基準ビルドからの増分。BOM は v1.3 → v2.0 へ改訂(台帳 10/20/30/31/32/33/34 は同一ファイルを改訂継続)
- V1 凍結オラクル(tag loop-v1-r1、S-01〜S-12)は回帰検査として不変のまま全ループ維持。
  V2 の新規固定オラクルは製造開始前に凍結する(tag loop-v2-r1)。
  playbook §6.2 規律: V1 で裁定済みの次元のうちオラクル昇格が必要なものは V2 凍結時に昇格
- ADR 必須(docs/adr/)。ビューア拡張で新規ライブラリ採用があれば ADR 追加
- SB-F-01 裁定の実施(製造開始前に基準ビルドへ適用 — §裁定記録 5)
- 非破壊原則の woven 格上げ: 「物理画像ファイルを移動・削除・結合しない」を V2 Phase 2 で
  20-spec の woven 安全制約へ格上げ(明文化)。merge/trash/permanent-delete 導入ループで
  L3 物理差分 CP を追加する(V2 ビューアは読み取り専用のため CP は明文化のみ)

## 裁定記録(2026-06-13 — 起案時にユーザー maintainer が確定)
| # | 項目 | 裁定 | 適用先 |
|---|---|---|---|
| 1 | タグ制御モード(viewer-spec §5) | V3 以降へ defer | 本チャーターのスコープ除外 |
| 2 | RVP-REPAIR-004(pending 消費の不整合) | improve-on-port — 「pending を replacement に使ったら削除」に統一 | 修復機能ループ(V3+)の仕様起点として記録 |
| 3 | RVP-REPAIR-005(detailed=pHash のみ) | UI を pHash-only 表示に是正(detailed 本実装はしない) | 同上 |
| 4 | RVP-MERGE-002(値付きタグの衝突) | マージ先優先 — マージ先に値があれば保持、なければマージ元の値を採用 | マージ機能ループの仕様起点として記録 |
| 5 | SB-F-01(依存ピンの浮動面) | exact ピン+global.json 固定 — MS.Ext.DI/Logging=10.0.9・Serilog.Extensions.Logging=9.0.2・Serilog.Sinks.File=7.0.0・SDK=10.0.100 | V2 起案時に実施(53-service-bom へ記録) |

## バックログ出典(起案入力のトレース)
- GF-01〜05: bomdd/50-as-built.yaml golden_findings(V1 golden 承認と独立の minor 所見)→ スコープへ取込
- RVP-UI-003: view-prism porting-spec 08(トレース表)+docs/view-prism-viewer-spec.md+
  原典ソースリバース(2026-06-13 設計者側調査: 4 モード完全実装・CustomNormalMode スタブ・
  タグ制御は設定 UI 未実装・設定永続化なし・キーボードは ←→/SHIFT+←→/Esc)
- porting-spec 未裁定 3 件: 裁定済み(§裁定記録 2〜4)。実装は該当機能ループで
- SB-F-01: bomdd/53-service-bom.yaml findings
- 明示 defer 群: bomdd/reports/eco-002-surface-crosscheck.md §8

## 完了の定義
- V2 固定オラクル(loop-v2-r1 凍結)全通過+V1 凍結オラクル(loop-v1-r1)回帰ゼロ
- blocker ずるゼロ
- 納品物: 成果物 + bomdd/ 改訂一式(BOM v2.0・オラクル・治具・As-Built・cheat-log・metrics)
- golden 承認: V2 新規 golden(ビューア拡張)+GF-01〜05 是正の承認者確認(承認者 maintainer)
