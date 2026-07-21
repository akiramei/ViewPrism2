# ECO-128 — ゴミ箱復元の安全側遷移 — T6(deleted→normal)を「復元後は原則 pending」へ(applied)

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

- **gate①(裁定)— 裁定済み(2026-07-21・§7)。残ゲートなし(着手条件= ECO-129 クローズ=満了)**。
- **gate②(golden)**: 是正完了・§9 に合格基準を提示(復元→pending の一覧挙動・実機)。

## 8. 実施記録(2026-07-21 fix)

- **R5(プローブ先行)**: CpTrash020Tests の可変 CP を新意味論へ書き換え=**是正前赤 2 本実測**
  (①復元 物理存在→Pending+origin=Restored ②純粋関数ベクタ ResolveRestore(true)=Pending)。
  T7(不在→missing・origin=NULL)は deleted が元々 origin なしのため是正前から緑=非退行を確認。
- **是正(最小)**: ①`TrashTransition.ResolveRestore(true)` を Normal→**Pending**(1 行・docstring T6→T6')
  ②`TrashService.RestoreAsync` で origin 導出(Pending→Restored / Missing→null)③`IImageRepository`+
  `ImageRepository` に **`RestoreStatusAsync(id,status,origin)`** 新設(status+pending_origin を単一 UPDATE で
  原子適用・candidate_link_id は deleted 行が常に NULL のため不変)。`PendingOrigin.Restored` は ECO-129 が
  予約済み・裁定 UI(PendingReviewViewModel)と i18n(ja/en の originRestored/whyRestored*)も完備=**追加 UI 不要**
  (gate① 見込みどおり)。App の 2 復元呼出元(画像タブ ImageTabTrashViewModel・作業タブ WorkTabViewModel)の
  docstring/コメントを T6' へ追随。
- **spec/BOM 改訂**: 遷移表 T6→**T6'(deleted→pending・origin='restored')**・§2.11.3 全面改訂(復元の責務=
  deleted 解除まで/normal へ自動昇格しない)・**INV-013 v5.0 強化**(復元だけで normal に戻さない=幽霊 normal 防止を
  包含)・OC-21 契約更新・golden G-10 文言(復元→未裁定バッジ)。
- **凍結オラクル処置(gate① 裁定=skip 化+理由刻印+新規行)**: **S-29**(復元→Normal を exact pin)を
  `[Fact(Skip=...)]`+41-fixed-oracle.yaml に `superseded_by: ECO-128` 刻印 → **新規 S-43**(復元→Pending+
  origin=Restored/不在→Missing+origin=NULL/normal 拒否/タグ ID 不変/純粋関数)で新意味論を pin。
  **【gate① 見積り不足の顕在化】** ECO-129 §8 は「真の衝突は 128 の S-29 のみ」と見積もったが、fix 中に
  **S-26(復元・完全削除の物理非破壊・L3)も復元→Normal を pin**して衝突と判明。同一衝突クラスのため
  gate① の裁定済み処置(skip 化+新規行)を**機械適用**: S-26 を skip 化+刻印 → **新規 S-44**(復元→Pending の
  物理非破壊+完全削除の物理非破壊=S-26 の全カバレッジを新意味論で承継)。**maintainer への申し送り**=
  gate① の「衝突は S-29 のみ」は不完全だった(S-26 も同処置)。凍結オラクルの既知 skip は **2→4 件**
  (S-29・S-26 追加)。
- **機械受入(4 点)**: build 0 error/0 警告・Tests **916/916**・Oracle **109+4skip**(既知 2+S-29+S-26)・
  validate 0/0。
- **R7(セルフゴールデン)**: 新規 UI サーフェスなし=**対象外を宣言**。復元 pending のバッジ/裁定 UI は
  ECO-129 の既存サーフェス(pending_review mock+FS ブラウズの未裁定バッジ)を再利用し、視覚は不変
  (結果 status が変わるのみ=gate① 見込みどおり)。captures 撮り直し不要。
- **R8(セルフレビュー・fresh context 独立)**: fix diff を独立 subagent でレビュー。所見 4 件=
  **スコープ内 1**(ImageTabTrashViewModel:142/149 の復元コメントが旧意味論のまま=diff が 2 呼出元の片方
  〔WorkTab〕だけ更新した対称漏れ)→**本 ECO 内で是正**(コメントを T6'/pending へ・挙動変更なし)。
  **スコープ外 3**(①作業タブに pending 導線がない=復元 pending が作業タブから不可視〔別 ECO 候補〕
  ②RestoreStatusAsync の deleted 限定 WHERE 不在〔防御的・非退行〕③作業タブ復元後のトラッシュバッジ
  件数未更新の疑い〔先在〕)→**51-cheat-log 記帳**。コア是正の設計健全性(状態機械の閉包=再スキャンで
  origin=Restored 維持・T12 で missing 化+タグ保全/消費サイト read-across=relink INV-015 ガード・normal 限定
  集計は事故なし)を独立確認。
- **diff 規模**: src 4 ファイル(Core 3=TrashTransition/TrashService/IImageRepository・Infra 1=ImageRepository)
  +App コメント 2 ファイル・spec 数節・oracle 2 skip 化+新規 2 クラス+yaml 2 superseded+2 新規・
  可変 CP 3 クラス追随(CpTrash020/CpTrash001/CpUiG1TrashPopup/CpUiG1WorkTab)+デコレータ 2 追随。

## 9. 停止点= golden 合格基準(gate②・実機)

1. **復元→未裁定(核)**: ゴミ箱で deleted 画像(記録パスに物理ファイルあり)を選び「復元」→
   一覧に **normal ではなく未裁定バッジ付き(pending)で現れる**。⋯メニュー「未裁定の画像… N」件数が増える。
2. **復元 pending の裁定**: ⋯「未裁定の画像」→裁定ダイアログで由来チップ「**復元**」(灰)・
   「ゴミ箱から復元された画像です」。受け入れる→normal 化(タグ保持)/削除する→ゴミ箱へ戻る/保留→残る。
3. **不在は missing(T7 不変)**: 物理ファイルが無い deleted を復元→missing として扱われる旨(normal に戻らない)。
4. **タグ保全**: タグ付き画像を削除→復元→pending でもタグが保持され、受け入れ後も残る。
5. **作業タブ復元の挙動差**(申し送り): 作業タブでゴミ箱から復元すると pending 化し、**作業タブ一覧には
   現れない**(normal 限定)。裁定は画像タブで行う。これを許容とするか=作業タブ pending 導線の要否は
   別 ECO 判断(cheat-log 記帳済み)。
6. **回帰**: 完全削除(物理非破壊・確認)・除外(missing→deleted)・二段階スキャン・pending 裁定 4 操作・
   26 万件ブラウズ体感。

合格なら `/eco-accept eco-128` を指示してください(基準 5 の作業タブ挙動差の裁定も添えて)。
不合格所見(GF-*)は本 ECO の手順 1 から。

## 10. クローズ(2026-07-22・golden 合格・ECO-131 と合同)

- **実機確認(maintainer・ECO-131 と合同)**: §9 基準 1〜4・6(復元→未裁定バッジ/裁定 4 操作+由来チップ
  「復元」/不在→missing/タグ保全/回帰)を承認。gate② 中に発見した **GF-128-01(クロスタブで画像タブが
  古い normal を表示)は ECO-131 で分離是正**し、合同で解消を確認(作業タブ削除/復元→画像タブ即時反映)。
- **基準 5 の裁定(作業タブ挙動差)**: 作業タブでゴミ箱から復元すると pending 化し作業タブ一覧には現れない
  (normal 限定・INV-W2)=裁定は画像タブで行う、という挙動を**許容と裁定**(2026-07-22)。作業タブ側 pending
  導線の要否は別 ECO 判断(51-cheat-log 記帳済み)。
- **再発防止**: CP-UI-G1 へ T6'(復元→pending・origin=Restored)観点+**「旧意味論を pin する凍結オラクルの
  棚卸しは全 depth 横断(L3 物理差分も status を incidental に pin し得る)」**を刻印。機械 pin=
  CpTrash020/CpTrash001(復元→pending+origin)+S-43(unit)+S-44(L3 物理非破壊)。
- **M4 同期**: 不要 — spec §2.11.3/遷移表 T6'/INV-013 v5.0/OC-21・migration(pending_origin は 129 の
  migration 010)は fix 時に登記済みで as-built 乖離なし。
- **凍結 skip の会計**: 既知 skip は **2→4 件**(S-29・S-26 追加)。機械受入の Oracle 期待は「109+4skip」。
- **教訓**(read-across 明記):
  1. **旧意味論を exact で pin する凍結オラクルの棚卸しは全 depth を横断する** — gate① は「真の衝突は
     S-29(unit)のみ」と見積もったが、fix 中に **S-26(L3 物理非破壊)も復元→Normal を incidental に pin**
     して衝突と判明。物理差分テストのような別目的のオラクルでも、意味論変更の対象値(ここでは復元後 status)を
     assert していれば衝突する。**凍結オラクルの grep は status enum など変更対象の全登場箇所で行い、
     depth(unit/L2/L3)で絞らない**(ECO-129 の「凍結オラクル棚卸しは全 depth 横断」の実証例・BomDD 昇格候補)。
  2. **状態機械の意味論変更は、後続の予約値・UI が既にあれば最小 diff で通る** — `PendingOrigin.Restored` は
     ECO-129 が予約し裁定 UI/i18n も完備していたため、ECO-128 の src 是正は実質「遷移 1 行+原子更新 API」で済んだ
     (3 分割設計の依存順=130→129→128 が効いた)。

1. **実施順序= 130→129→128 を承認**。本 ECO は最後尾(ECO-129 の pending 意味論
   〔行削除廃止・可視化+バッジ・裁定導線〕成立後に着手= §3 事実 4 の依存が解消されてから)。
2. **S-29 固定オラクル行の処置= 案(i) skip 化+理由刻印+新意味論行の追加**
   (3 ECO 一括裁定・ECO-129/130 §7 と同一)。
3. 本 ECO 固有の追加 mock は不要見込み(復元導線は既存・結果 status が変わるのみ。
   pending バッジの視覚は ECO-129 の CAD mock に含まれる)。
