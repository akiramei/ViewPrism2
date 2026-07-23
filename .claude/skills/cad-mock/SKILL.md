---
name: cad-mock
description: mock→CAD化(ViewPrismUI)。ECO の gate① 裁定を受けた UI 変更の視覚原器を、逸脱ゼロ・商標/PII 混入ゼロで作る。新納品 mock HTML+サーフェス captures+画面正典/マトリクス更新を再現可能な手順で行い、golden(maintainer 実機承認)で停止する。UI 面の追加・改訂の mock/captures 作成はここが入口。
---

# /cad-mock — mock→CAD化(mock 納品+captures+契約+サニタイズ)

出自: 2026-07-23 ECO-139 の mock/captures 作成で得た運用知の工具化。ViewPrismUI `CLAUDE.md`「CAD化の手順」の
ハーネス化。方法論の親= mock-ui-ir-is-cad / viewprismui-cad-repo(CAD が正・golden-in-the-loop)。

引数: 対象(`<ECO-NNN>` または `<画面名> <追加/変更する面>`)。例: `ECO-139` / `pending_review PD-5,PD-6`。

CAD リポ= `../ViewPrismUI`。製品コード(`../ViewPrism2/src`)はこのスキルで触らない(=/eco-fix の仕事)。

## 絶対規律

- **一次資料は不改変**: `資料/<画面>/` の既存 mock は編集しない。**差し替え・追加は新納品 HTML**(新ファイル)で行う
  (ViewPrismUI CLAUDE.md 禁止事項)。
- **サニタイズ・ゲート(混入ゼロが納品条件)**: mock のサンプルデータに**実在の商標/商業製品名・個人情報・
  実個人の実パス・第三者著作物の実画像を入れない**。中立プレースホルダのみ。**captures 撮影前(ゲート①)と
  生成画像の目視(ゲート②)の 2 回**走査する。混入は原器に固着し golden で下流へ流れる(ECO-139 で
  実在ゲームのタイトルがサンプル行に混入、目視で偶然検出した実績=運任せを恒久対策化)。
- **captures は再現レシピで作る**: プロンプト/勘で撮らず、同梱 `shoot.py` で撮る(遅延生成のポーリング・
  autocrop を内包)。既存 captures と寸法の桁を揃える。
- **Provisional 明記**: mock/captures は AI 起草の設計提案。**golden 承認までは Provisional** と全成果物に明記。
- **未確定を発明しない**: mock に無い事柄(閾値・入口・空状態等)は `review_points.md` へ登録して残す。

## サニタイズ・チェックリスト(混入禁止 → 中立置換)

| 種別 | 混入例 | 中立置換 |
|---|---|---|
| 商標/商業製品名(ゲーム・ブランド・アプリ) | `<実在ゲーム名>_1784.png` | `img_2207.png` / `photo` / `scan` / `album` |
| 実個人名・ハンドル・メール・電話・住所 | `<実名>@<実ドメイン>` | 汎用サンプル(`taro@example.com`・「山田」等)・伏字にしない中立名 |
| 実在の絶対パス(ユーザープロファイル配下等) | 実マシンのプロファイル絶対パス | 例示パス(`2024\旅行\` 等の相対・仮想パス) |
| 第三者著作物の実サムネイル/実写 | 実画像の貼付 | CSS グラデ等の抽象プレースホルダ(mock に実画像を貼らない) |
| EXIF/GPS/ウォーターマーク風テキスト | 実座標・実機種 | 除去 or 明示ダミー |

## 手順

1. **入力確定**: `<ECO>` なら `bomdd/60-change-order-eco-NNN.md` の CADモック指示節(例 ECO-139 §4.1)を読む。
   `<画面名>` なら `../ViewPrismUI/docs/screens/<画面>.md` を読む。追加/変更する面(PD-N)と設計意図・gate① 裁定を確定。
2. **mock 納品作成**: 既存 mock の**トークン/CSS に準拠**して新納品 HTML を `資料/<画面>/…(standalone).html` に作る
   (`?face=PD-N` 単面対応の `<script>` を含める)。v1 一次資料は不改変。
3. **サニタイズ ゲート①**: 上表で mock のサンプル値を走査し、混入を中立名へ是正してから撮影へ進む。
4. **captures 生成**: 同梱ツールで撮る。
   `python .claude/skills/cad-mock/shoot.py --html "<mock.html 絶対パス>" --faces PD-5,PD-6 --outdir "../ViewPrismUI/docs/screens/captures/<画面>" [--win-for PD-6=900,520]`
   (要 PIL。msedge --headless=new の screenshot は遅延生成 → ツールがポーリング → PIL autocrop。既存 captures の桁に合わせる)。
5. **目視+サニタイズ ゲート②**: 生成画像を Read で目視し、①レンダリング忠実性 ②**画像内に商標/PII テキストが
   写っていないか**を確認。混入があれば mock を是正して 3〜5 を再走。
6. **CAD 契約更新**: `docs/screens/<画面>.md` の一次資料表・capture 参照・レイアウト/視覚契約チェックリスト・
   状態/インタラクション/バリデーションを面に合わせ更新。ダイアログ面なら `docs/03_dialog_language.md` の
   **適用面マトリクスへ行追加**(L1〜L8 の該当列)。`review_points.md` の関連裁定(PEND-*/等)を更新。
   README/00_project_overview は**画面を増やしたときのみ**更新(面追加は不要)。
7. **コミット**: ECO 紐付きは `decide(eco-NNN): <要約>`、それ以外は `docs(<画面>): <要約>`。Provisional 明記。
   資料/ の新納品 HTML・captures PNG・画面正典・マトリクスをまとめてコミット(製品 src は含めない)。

## 停止点(human gate = golden)

**maintainer 実機承認(golden)で停止**する。「人間がやること= PD-N を実機/captures で確認し golden 承認
(視覚契約チェックリスト+可逆性等)」を明示して止める。承認後に製品コードは `/eco-fix eco-NNN`(=別スキル)。

## 実績・注記

- ECO-139 pending_review PD-5(自動裁定 callout)/PD-6(CMP-011 確認)で初適用。captures= headless Edge 実寸
  (PD-5 800x638・PD-6 520x262)。サニタイズで実在ゲーム名→img_2207.png を是正。
- `shoot.py` の要点: screenshot 遅延生成のポーリング必須(即 ls は空)・autocrop(bg=角ピクセル・threshold 12・
  margin 12)・`--force-device-scale-factor=1` で実寸。
