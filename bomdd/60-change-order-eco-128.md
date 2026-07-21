# ECO-128 — ゴミ箱復元の安全側遷移 — T6(deleted→normal)を「復元後は原則 pending」へ(staged)

- 起票日: 2026-07-21
- 報告者: maintainer 設計まとめ(2026-07-21「画像の状態管理・あるべき状態管理」§6)の投入。3 分割の (a)
- 種別: 仕様改訂候補(状態機械 T6 の意味論変更。実装逸脱ではない)
- baseline: ViewPrism2 main `390e1f4`
- 関連: **ECO-129(pending 意味論の再定義)に依存** / ECO-130(スキャン二段階化)と同一設計まとめ起点

---

## 1. 症状(変更要求)

現行のトラッシュ復元は「記録パスに物理ファイルが存在すれば status=normal(T6)」。
maintainer 設計まとめ §6 はこれを安全側へ倒す:

> 復元操作の責務は、`deleted` という管理上の除外を解除することまで。復元だけで `normal` へ戻すと、
> スキャンで大量発見された未裁定画像を削除し、後から復元したケースでも、自動的に受入れ済みになってしまう。

- 新遷移: deleted 復元 → ファイルあり= **pending** / ファイルなし= missing(T7 は不変)
- 安全性は状態遷移で確保し、ユーザーの作業負担は修復・自動修復機能で軽減する

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI trash 面) | 追随要否の確認のみ | 復元の結果 status を明記した mock 文言は要確認。復元導線自体は既存(視覚変更なし見込み) |
| BOM/仕様(20-spec §2.11.3・T6・OC-21・INV-013) | **改訂対象(上流)** | T6「deleted→normal」は v4.0 G2 裁定 B-2 の産物であり当時は正。新モデル(pending=未裁定の管理状態)の下では「復元=自動受入れ」となり設計まとめ §6 と矛盾 |
| 実装 | 健全(現仕様に忠実) | [TrashTransition.cs:15](../src/ViewPrism2.Core/Services/Repair/TrashTransition.cs) `ResolveRestore(true)=Normal` = 仕様どおり。実装逸脱ゼロ |

**結論: 仕様層の設計変更。上流(spec)改訂 → 実装追随の順。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **現行実装**: `TrashTransition.ResolveRestore(fileExists)` = 存在→Normal / 不在→Missing
   (純粋関数・[TrashService.cs:60](../src/ViewPrism2.Core/Services/Repair/TrashService.cs) が唯一の消費者)。
2. **固定オラクル衝突(R6)**: S-29(41-fixed-oracle.yaml:261)が
   「存在→status=Normal(T6)/ 不在→status=Missing(T7)」を exact で pin。意味論変更は
   「既存固定オラクル行は変更しない」規約と正面衝突する = **裁定が必要**。
3. **INV-013 の射程**: 現行の「幽霊 normal 防止」はファイル不在時のみ。設計まとめ §6 は
   さらに強く「復元だけで normal 禁止」= INV-013 の強化(精密化)として書ける。
4. **依存の発見(起票時診断)— 本 ECO は「独立最小」ではない**。現行 pending 意味論
   (=新規発見ファイル側の relink 候補・未タグ前提・INV-010 で既定一覧に不可視)のまま
   復元→pending を先行させると:
   - ① 復元した画像が全一覧・全ビューから**不可視**になり、pending 裁定 UI も存在しないため
     「復元したのに消えた」体験になる(INV-010 + 裁定導線不在)。
   - ② スキャン手順 5([ScanService.cs:163](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs))が
     ファイル消失 pending を**行削除**する。復元後にファイルが消えると、タグ付き画像でも行ごと
     消滅= **タグ損失**(現行 INV-015 の「pending は新規スキャンで未タグ」前提が破れるため)。
   - ③ relink 候補プール(pending ∪ untagged-normal)に入る。untagged の復元画像は他の missing の
     修復に**消費されて行削除**され得る(現行 T4 の正当動作が新文脈では意図外)。
   → **ECO-129 の pending 意味論確立(行削除廃止・可視化/裁定導線)が前提**。

### 未検証(疑い)

- トラッシュ UI(ImageTabTrashViewModel)の復元結果の文言・件数表示が status 名に依存するか(fix 時確認)。
- タグ付き pending(復元由来)が relink 候補列挙に現れた場合の表示(タグ安全ガードで確定は拒否される
  ことは確認済み — 列挙段階の除外も既存実装にあり [RelinkService.cs:78](../src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs))。

## 4. 是正方針(案・着手時確定)

**案A(推奨・設計まとめ字義どおり)**: `ResolveRestore(true)` → `Pending`(1 行)+
spec §2.11.3/T6 改訂(T6': deleted→pending)+ INV-013 強化 + OC-21 契約更新 + oracle 新規行。
復元後の pending は ECO-129 の裁定導線で normal 化する。

diff 規模: src 1 行+docstring・spec 数節・tests(OC-21 unit 追随+oracle 新規行)。
視覚変更なし見込み(golden は復元後の一覧挙動確認)。

**前提**: ECO-129 適用済み(推奨実施順序= ECO-130 → 129 → 128)。

## 5. 影響 BOM

- **src**: TrashTransition.cs(1 行)+ TrashService docstring
- **spec**: §2.11.3(T6 → T6')・§2.11.0 遷移表・INV-013 強化
- **tests**: OC-21 unit 追随+oracle 新規行(S-29 の処置は gate① 裁定に従う)
- **CAD**: trash 面の復元説明文言の確認(乖離あれば ViewPrismUI 側へ申し送り)
- **CP**: CP 刻印は accept 時

## 6. 残ゲート

- **gate①(裁定)— 裁定済み(2026-07-21・§7)。残ゲートなし(着手条件= ECO-129 クローズ)**。
- **gate②(golden)**: 是正後に提示(復元→pending の一覧挙動・実機)。

## 7. gate① 裁定記録(2026-07-21・maintainer)

1. **実施順序= 130→129→128 を承認**。本 ECO は最後尾(ECO-129 の pending 意味論
   〔行削除廃止・可視化+バッジ・裁定導線〕成立後に着手= §3 事実 4 の依存が解消されてから)。
2. **S-29 固定オラクル行の処置= 案(i) skip 化+理由刻印+新意味論行の追加**
   (3 ECO 一括裁定・ECO-129/130 §7 と同一)。
3. 本 ECO 固有の追加 mock は不要見込み(復元導線は既存・結果 status が変わるのみ。
   pending バッジの視覚は ECO-129 の CAD mock に含まれる)。
