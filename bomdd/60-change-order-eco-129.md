# ECO-129 — pending 意味論の再定義 — 「relink 候補」から「存在するが未裁定」の管理状態へ(スキャン判定の pending 化+裁定導線)(staged)

- 起票日: 2026-07-21
- 報告者: maintainer 設計まとめ(2026-07-21「画像の状態管理・あるべき状態管理」§2/§3/§7/§13)の投入。3 分割の (b)
- 種別: 仕様改訂候補(状態機械の中核意味論変更+UI 新設。実装逸脱ではない)
- baseline: ViewPrism2 main `390e1f4`
- 関連: **ECO-130(スキャン二段階化)の先行または同時を前提**(maintainer 指示済み)/ ECO-128 は本 ECO に依存

---

## 1. 症状(変更要求)

3 分割の中核。現行の pending は「新規発見ファイル側に付く再リンク候補」(candidate_link_id 付き・
未タグ前提・消えたら行削除)という狭い意味論。設計まとめはこれを
**「ファイルは存在するが、ViewPrism 上の扱いが未裁定」の管理状態**へ再定義する:

| 事象 | 現行 | 新モデル |
| --- | --- | --- |
| 内容変更(パス同一・ハッシュ不一致) | hash/size/日時を黙って更新・status 不変(規則 2) | **pending**(編集か差し替えかは機械判定しない — §3) |
| 新規発見 | normal で即登録(規則 3b / T1) | **新規 pending**(§2/§13) |
| pending のファイル消失 | **行削除**(手順 5) | **missing 保持**(§7: pending→missing→pending) |
| pending の裁定 | relink 確定(T4)のみ | **裁定導線新設**: 編集後として受入れ / 別画像として扱う / 削除 / 保留(§3) |

運用原則(§13): 機械観測とユーザー裁定を分離・不確実は pending に倒す・自動判定より候補提示。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **未定義 — mock 先行が必要** | pending 裁定 UI(受入れ/別画像/削除/保留)の mock・screen_spec が不在。pending の一覧表示(バッジ等)も未定義 |
| BOM/仕様(20-spec §2.1 遷移表・§2.11.0 T1/T2・手順 5・OC-5・INV-010/INV-015) | **改訂対象(上流)** | 現仕様 v4.0 は旧意味論で自己整合しており欠陥ではない。新モデルへの改訂 |
| 実装 | 健全(現仕様に忠実) | [ScanJudge.cs:64](../src/ViewPrism2.Core/Services/ScanJudge.cs)(規則 2)・:87(規則 3b)・[ScanService.cs:163](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs)(手順 5)= 全て仕様どおり |

**結論: 仕様層の設計変更+CAD 新設。CAD mock → spec 改訂 → 実装追随の順。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **現行実装サイト**: ScanJudge 規則 2(status 不変の UpdateMeta)・規則 3b(AddNormal)・
   ScanService 手順 5(pending 行削除)・手順 4(normal→missing)。いずれも spec v4.0 と一致。
2. **固定オラクル衝突(R6)**: OC-5 は「遷移 4 ケースを exact」で pin(spec:661)。
   S-01(リネーム追跡 E2E)は「初回スキャン 3 枚→タグ付与→…」の前提が**新規=normal 登録**に
   依存(新規= pending だと未裁定画像へのタグ付与という別問題になる)。規則 3a
   (同ハッシュ missing への candidate 付き pending)自体は新モデルでも整合するが、
   周辺の exact 行は旧意味論を pin = **処置裁定が必要**(ECO-128 の S-29 と同一論点)。
3. **INV-010 との衝突が新規に生じる**: 内容変更→pending にすると、既定一覧・ビュー評価は
   normal のみ対象のため、**タグ付き画像がファイル編集しただけで全ビューから消える**。
   現行モデルでは起きない UX(規則 2 が status 不変のため)。pending の可視化方針の裁定が必要。
4. **INV-015 の前提が破れる**: 「pending は新規スキャンで未タグ」は新モデルでは偽
   (内容変更→pending は既存タグを保持したまま pending 化する)。relink のタグ安全ガード自体は
   実タグ照会なので堅牢だが、**手順 5 の行削除は無条件のため、タグ付き pending の行ごと消滅=
   タグ損失**を起こす。行削除の廃止(missing 保持)は安全性の要(§7)。
5. **candidate_link_id は新モデルでも有用**: 「同フォルダ同ハッシュの missing あり」は
   pending の裁定候補ヒント・自動修復候補として引き続き機能する(規則 3a は「pending の亜種」
   から「pending +候補ヒント」へ自然に再解釈できる)。
6. **規模の含意**: 新規→pending は、適用前確認(ECO-130)なしでは初回登録や大量追加で
   裁定対象が爆発する(26 万件フォルダ登録= 26 万 pending)。maintainer 指示済み=
   **ECO-130 を先行または同時**。

### 未検証(疑い・着手時確定)

- 初回スキャン(isInitialScan)の特例扱いの要否(§6 裁定事項 1)。
- pending 裁定 4 操作の遷移先詳細(「別画像として扱う」= 旧レコードとの関係をどう記録するか。
  設計まとめ §7 は「状態履歴の厳密管理はしない」方針)。
- NodeGraph・類似検索・エクスポート等、status=normal 前提の全消費サイトの棚卸し
  (spec INV-010 の適用面。fix 時に grep 悉皆)。

## 4. 是正方針(案・着手時確定)

段階: **CAD mock(ViewPrismUI)→ spec 改訂(遷移表・INV 改訂・REQ 採番)→ 実装追随**。

- ScanJudge: 規則 2 → UpdateMeta+**status=pending**(normal 起点のみ。missing/deleted 起点の扱いは
  spec で規定)・規則 3b → AddPending(候補ヒントなし)。判定器は純粋関数のまま(OC-5 の器は維持)。
- ScanService: 手順 5 廃止 → pending のファイル消失は missing へ(手順 4 の対象拡張)。
- 裁定導線: pending 一覧+4 操作(受入れ=normal 化 / 別画像=新レコード分離等 / 削除=deleted /
  保留)。CAD mock 確定後に BOM 展開。
- INV-010/INV-015/T1/T2 改訂+新遷移(pending→normal 裁定・pending→missing・missing→pending 再出現)。

diff 規模: 大(spec 中核+UI 新設+テスト群)。**26 万件経路の性能規律を適用**
(pending 件数に比例する処理を裁定画面外へ漏らさない)。

## 5. 影響 BOM

- **CAD**: pending 裁定 UI+pending 可視化の mock/screen_spec 新設(ViewPrismUI・先行)
- **spec**: §2.1 判定規則 2/3b・手順 5・§2.11.0 遷移表(T1/T2 改訂+裁定遷移追加)・
  INV-010/INV-013/INV-015 改訂・REQ 新設(採番は spec 改訂時)
- **src**: ScanJudge・ScanService・裁定サービス/VM/View 新設・INV-010 消費サイト追随
- **tests**: OC-5 追随+oracle 新規行(旧行処置は gate① 裁定)・裁定遷移 unit・UI probe
- **CP**: 新設は accept 時

## 6. 残ゲート

- **gate①(裁定)— §7 で裁定済み(2026-07-21)。残= CAD mock**:
  1. ~~初回スキャンの新規の扱い~~ → **裁定済み(§7= 初回のみ normal)**。
  2. ~~pending の可視化~~ → **裁定済み(§7= 既定一覧に出す+バッジ)**。
  3. ~~固定オラクル旧行の処置~~ → **裁定済み(§7= skip 化+刻印+新規行)**。
  4. **CAD mock の作成**(ViewPrismUI 側 gate: 裁定 UI・pending バッジ表示)— **未・維持者作業**。
- **gate②(golden)**: 是正後に提示(裁定 UI・26 万件実機を含む)。

## 7. gate① 裁定記録(2026-07-21・maintainer)

1. **実施順序= 130→129→128 を承認**(ECO-130 先行=適用前確認の器が先)。
2. **初回スキャンの新規= 初回のみ normal(案 ii)**。フォルダ登録という明示操作を裁定とみなし、
   初回スキャン(isInitialScan)の新規登録だけ normal。**通常スキャンの新規は pending**(設計まとめ
   どおり)。26 万件登録の裁定爆発を回避しつつ「再出現しただけで無条件 normal にしない」原則は維持。
3. **pending の可視化= 既定一覧に出す+未裁定バッジ(INV-010 改訂)**。外部編集しただけで画像が
   一覧から消える UX を回避する。ビュー評価・NodeGraph・類似検索・エクスポート等の各消費サイトでの
   pending の扱いは spec 改訂時に面ごとに規定(必要な追加裁定は spec 改訂中に個別提示)。
   バッジの視覚は CAD mock で確定。
4. **固定オラクル旧行の処置(3 ECO 一括)= skip 化+理由刻印+新意味論の新規行追加**
   (ECO-130 §7 と同一裁定。対象= OC-5 exact 行・S-01 の旧前提部ほか、spec 改訂時に全数特定)。
5. 残 gate①= CAD mock(pending 裁定 UI= 受入れ/別画像/削除/保留+一覧バッジ)。受領後に /eco-fix eco-129。
