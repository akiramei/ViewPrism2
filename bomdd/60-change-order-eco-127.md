# ECO-127 — CP-REGISTRY-LINT-122 allowlist 根拠の張り替え — REG-C6/C7 裁定への追随(doc-only)(applied)

- 起票日: 2026-07-21
- 報告者: BomDD 側 → VP2 短信(ECO-122 検収反映=REG-C6/C7 裁定の受領)
- 種別: 検査台帳の根拠文字列更新(doc-only・実装/挙動/検査ロジック不変)
- baseline: ViewPrism2 main `3271116` / ViewPrismUI `fcaee3f`(REG-C6/C7 裁定済み)

---

## 1. 要求

ECO-122 検収を受けて CAD 側が 2 件の未収束を裁定した(ViewPrismUI `fcaee3f`・2026-07-21・maintainer):

- **REG-C6**: CMP-004 SegmentedControl に `menu-inline` バリアント追加(並び替えメニューの昇降
  セグメント= 非アクティブ地 `#f4f6fa`・アクティブ青地 `#eaf1fe`= VC-FL-1⑤ 正典値)。あわせて
  CMP-006 インスタンス契約へ並び替えメニューの `padding 0`・影 `0 22px 50px -16px rgba(20,32,64,.36)`
  を正典値として補完(ECO-122 R8 所見8「As-Built 乖離リスト不在」への回答= file_list mock 実測)。
- **REG-C7**: chip overflow ポップオーバー(`Border.chipPopCard`)を実装追認(幅 360・padding 10 の
  インスタンス契約値として CMP-006 へ記帳。裁定時実測で地は白= クローム一致=起票時「地の不一致」は
  誤認)。

いずれも CAD が「VP2 は lint allowlist の根拠を更新可」と明記。**実装変更は不要**= CpRegistryLintTests
の allowlist 根拠文字列(2 概念・3 エントリ)を「判定待ち/暫定」から「裁定済み」へ張り替えるのみ。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 健全(裁定済み) | REG-C6/C7 が ViewPrismUI `fcaee3f` に記録・両方 maintainer 裁定済み・「VP2 更新可」明記 |
| BOM | 健全 | 検査ロジック・tolerance 不変 |
| 実装(src) | **無変更** | dlgBtn/popupMenu/chipPopCard の視覚・クラスとも不変(REG-C6/C7 とも「実装変更なし」裁定) |
| tests | **根拠文字列のみ更新** | CpRegistryLintTests の allowlist 値(Dictionary の value= 根拠。lint ロジックは key のみ参照= 挙動ゼロ影響) |

**結論: 検査台帳の根拠文字列のみの doc-only 更新。裁定不要(CAD 側で裁定済み)。**

## 3. 切り分け済みの事実

### 確定

1. **allowlist の value は lint 挙動に非関与**: 検査A/B とも `!allowlist.ContainsKey(f)` で key のみ判定。
   value(根拠文字列)は診断メッセージにも使わない= 張り替えは挙動ゼロ影響(tests は緑のまま)。
2. **射程の突合(over-claim 回避)**:
   - `Width=252:CornerRadius|Padding|BoxShadow`(2 エントリ)= 並び替えメニュー Border のクローム上書き。
     CornerRadius=13 → **REG-C3**(既記載)。Padding=0・BoxShadow → **CMP-006 インスタンス契約値の
     補完**(REG-C6 同日・file_list mock 実測)。メニューが内包する昇降セグメントが **REG-C6 の
     menu-inline バリアント**。
   - `chipPopCard`(1 エントリ)= REG-C7 で実装追認(CMP-006 インスタンス契約=幅360/padding10)。
3. **REG-C6/C7 の git 裏取り済み**: ViewPrismUI `fcaee3f`(2026-07-21)。作話でない(ECO-119 教訓)。

### 未検証(fix で確定)

- 張り替え後の tests 全緑維持・validate 0/0(doc-only の機械証拠)。

## 4. 是正方針(案・着手時確定)

**案A(唯一)**: allowlist 根拠文字列を張り替え:

1. `chipPopCard`: 「専用クローム(写像未検証・CAD 側判定待ち)」→
   「REG-C7 裁定済みインスタンス契約(chipPopCard 実装追認=幅360/padding10・地は白でクローム一致)」
2. `Width=252` ×2: 「radius13(REG-C3 裁定…)」→ radius13=REG-C3・padding0/影=CMP-006 インスタンス
   契約値補完(REG-C6 同日)・menu-inline セグメント=REG-C6 を反映した根拠へ。
3. 該当コメント行(120 行の「radius 13 はインスタンス契約差として REG-C3 裁定で確定」)も
   REG-C3/C6 の現況へ更新。

実装(src)は一切触らない。

## 5. 影響 BOM

- **tests**: CpRegistryLintTests の allowlist 根拠文字列(2 概念・3 エントリ)+説明コメント
- **src**: 無変更
- **CAD**: 無変更(REG-C6/C7 は CAD 側で記録済み)
- **CP**: CP-REGISTRY-LINT-122 の tolerance/検査ロジック不変(根拠の出典が判定待ち→裁定済みへ)

## 6. 残ゲート

- **gate①(裁定)**: 不要(REG-C6/C7 は CAD 側で裁定済み)
- **gate②(golden)**: n/a(doc-only・視覚/挙動/検査ロジック不変。機械証拠= tests 緑+validate 0/0=
  ECO-111/120 前例)

## 7. 実施記録+クローズ(2026-07-21・applied)

- **是正= 案A**: allowlist 根拠 3 エントリ+説明コメント 2 箇所を張り替え:
  - `chipPopCard`: 「専用クローム(写像未検証・CAD 側判定待ち)」→「REG-C7 裁定済みインスタンス契約
    (chipPopCard 実装追認・幅360/padding10・地は白でクローム一致)」。
  - `Width=252` ×2: 「radius13(REG-C3 裁定…)」→「REG-C3/C6 裁定済みインスタンス契約
    (radius13=REG-C3・padding0/影=REG-C6 補完・menu-inline バリアント含む)」。
  - 説明コメントも判定待ち/暫定→裁定済みの現況へ。実装(src)は無変更。
- **射程の正確性**: allowlist が覆う要素(Border クローム上書き/chipPopCard クラス)と裁定の射程を
  突合して根拠を書いた(REG-C6 の menu-inline はセグメント= メニュー内包要素・padding0/影は
  CMP-006 補完= 別々に帰属)。REG-C6/C7 は CAD `fcaee3f` で git 裏取り済み(作話でない)。
- **機械受入(4 点・張り替え前後で不変= doc-only の機械証拠)**: フルビルド 0 error・0 警告 /
  Tests **865/865** / Oracle 109+2skip / validate 0/0。
- **R5**: doc-only につきプローブ非該当(allowlist value は lint 挙動非関与= 張り替えは挙動ゼロ影響)。
  **R7/R8**: 対象外宣言(doc-only・ECO-111/119 型)。
- **検収= 機械証拠**(gate② n/a): ①根拠が裁定(REG-C6/C7)を正しく引用・射程一致 ②tests 緑・
  validate 不変(張り替え前後で同一)。ECO-111/120/121 前例。
- **教訓**: **allowlist の「判定待ち」根拠は CAD 裁定で確定へ昇格する — 検査台帳は契約の未収束を
  可視化する場でもある**(ECO-121「照合先の昇格は正本の検収イベント」の allowlist 版)。lint
  first-run が CAD 申し送りを生み(ECO-122 R8)→ CAD 裁定(REG-C6/C7)→ allowlist 根拠確定、の
  一巡が「照合先一元化」の運用サイクルの実証。

## 8. 停止点(起票時)

裁定は不要です。`/eco-fix eco-127` で張り替えに着手できます。
